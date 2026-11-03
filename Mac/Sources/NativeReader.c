#include "NativeReader.h"
#include <PCSC/winscard.h>
#include <IOKit/IOKitLib.h>
#include <IOKit/hid/IOHIDManager.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>

#define BR_CTL(n) (0x42000000u + (n))
#define BR_AMBIGUOUS ((int32_t)0x80100011)
struct BRReader { SCARDCONTEXT context; SCARDHANDLE card; uint32_t control; char name[1024]; };
static int32_t exchange_code(BRReader *r, uint32_t code, const uint8_t *in, uint32_t n, uint8_t *out, uint32_t *outn) {
    uint32_t received = 0;
    int32_t result = SCardControl(r->card, code, in, n, out, *outn, &received);
    if (result == SCARD_S_SUCCESS) *outn = received;
    return result;
}
void br_close(BRReader *r) {
    if (!r) return;
    if (r->card) SCardDisconnect(r->card, SCARD_LEAVE_CARD);
    if (r->context) SCardReleaseContext(r->context);
    free(r);
}
const char *br_name(BRReader *r) { return r ? r->name : ""; }
int32_t br_open(BRReader **result) {
    *result = NULL;
    BRReader *r = calloc(1, sizeof(*r)); if (!r) return SCARD_E_NO_MEMORY;
    int32_t status = SCardEstablishContext(SCARD_SCOPE_SYSTEM, NULL, NULL, &r->context);
    if (status != SCARD_S_SUCCESS) { br_close(r); return status; }
    uint32_t n = 0; status = SCardListReaders(r->context, NULL, NULL, &n);
    if (status != SCARD_S_SUCCESS || n < 2 || n > 65536) { br_close(r); return status ? status : SCARD_E_NO_READERS_AVAILABLE; }
    char *names = calloc(n + 1, 1); if (!names) { br_close(r); return SCARD_E_NO_MEMORY; }
    status = SCardListReaders(r->context, NULL, names, &n);
    if (status == SCARD_S_SUCCESS) {
        int found = 0;
        for (char *p = names; p < names + n && *p; p += strlen(p) + 1) {
            if (strcasestr(p, "OMNIKEY") && (strstr(p, "5427") || strstr(p, "5127"))) {
                // Multiple slots on one physical device are allowed; the first contactless slot is used.
                if (!found || strcasestr(p, "CL 0")) { snprintf(r->name, sizeof(r->name), "%s", p); found = 1; }
            }
        }
        if (!found) status = SCARD_E_NO_READERS_AVAILABLE;
    }
    free(names);
    if (status != SCARD_S_SUCCESS) { br_close(r); return status; }
    char fingerprint[256]; int mode = 0;
    if (br_usb_info(fingerprint, sizeof(fingerprint), &mode) > 1) { br_close(r); return BR_AMBIGUOUS; }
    uint32_t protocol = 0;
    status = SCardConnect(r->context, r->name, SCARD_SHARE_DIRECT, 0, &r->card, &protocol);
    if (status != SCARD_S_SUCCESS) { br_close(r); return status; }
    uint8_t features[512]; uint32_t featureLength = sizeof(features);
    if (exchange_code(r, BR_CTL(3400), NULL, 0, features, &featureLength) == SCARD_S_SUCCESS) {
        for (uint32_t p = 0; p + 2 <= featureLength;) {
            uint8_t tag = features[p++], len = features[p++];
            if (len > featureLength - p) break;
            if (tag == 0x13 && len == 4) r->control = ((uint32_t)features[p] << 24) | ((uint32_t)features[p+1] << 16) | ((uint32_t)features[p+2] << 8) | features[p+3];
            p += len;
        }
    }
    // Discover a usable escape channel using a READ ONLY board-type request.
    // Never try alternative control codes after a mutating APDU.
    const uint8_t probe[] = {0xFF,0x68,0,0,0x0A,0,0,0,0x0D,3,1,2,0,5,0,0};
    uint32_t codes[] = { r->control, BR_CTL(3500), BR_CTL(1) };
    status = SCARD_E_UNSUPPORTED_FEATURE;
    for (size_t i = 0; i < 3; i++) {
        if (!codes[i]) continue;
        uint8_t response[512]; uint32_t size = sizeof(response);
        status = exchange_code(r, codes[i], probe, sizeof(probe), response, &size);
        if (status == SCARD_S_SUCCESS && size >= 2 && response[size-2] == 0x90 && response[size-1] == 0) { r->control = codes[i]; *result = r; return 0; }
        if (status == SCARD_S_SUCCESS) status = SCARD_E_UNSUPPORTED_FEATURE;
    }
    br_close(r); return status;
}
int32_t br_exchange(BRReader *r, const uint8_t *input, uint32_t length, uint8_t *output, uint32_t *outputLength) {
    if (!r || length > 262 || !outputLength) return SCARD_E_INVALID_PARAMETER;
    return exchange_code(r, r->control, input, length, output, outputLength);
}
int32_t br_presence(BRReader *r, int *present) {
    if (!r) return SCARD_E_INVALID_HANDLE;
    SCARD_READERSTATE state; memset(&state, 0, sizeof(state));
    state.szReader = r->name; state.dwCurrentState = SCARD_STATE_UNAWARE;
    int32_t status = SCardGetStatusChange(r->context, 1000, &state, 1);
    if (status != SCARD_S_SUCCESS) return status;
    if (state.dwEventState & (SCARD_STATE_UNKNOWN | SCARD_STATE_UNAVAILABLE)) return SCARD_E_READER_UNAVAILABLE;
    *present = (state.dwEventState & SCARD_STATE_PRESENT) != 0; return 0;
}
static int integer(CFTypeRef v) { int result = 0; if (v && CFGetTypeID(v) == CFNumberGetTypeID()) CFNumberGetValue(v, kCFNumberIntType, &result); return result; }
static int property(io_registry_entry_t dev, CFStringRef key) { CFTypeRef value = IORegistryEntryCreateCFProperty(dev, key, kCFAllocatorDefault, 0); int n = integer(value); if(value) CFRelease(value); return n; }
static IOHIDManagerRef hid_manager(void) {
    IOHIDManagerRef m = IOHIDManagerCreate(kCFAllocatorDefault, kIOHIDOptionsTypeNone);
    int vendor = 0x076B; CFNumberRef value = CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &vendor);
    const void *key = CFSTR(kIOHIDVendorIDKey); const void *val = value;
    CFDictionaryRef match = CFDictionaryCreate(kCFAllocatorDefault, &key, &val, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    IOHIDManagerSetDeviceMatching(m, match); CFRelease(match); CFRelease(value); return m;
}
static int supported_hid(IOHIDDeviceRef d) { int p = integer(IOHIDDeviceGetProperty(d, CFSTR(kIOHIDProductIDKey))); return p == 0x5427 || p == 0x5428; }
int br_usb_info(char *fingerprint, size_t capacity, int *mode) {
    if (capacity) fingerprint[0] = 0; *mode = 0;
    io_iterator_t iterator = 0; int count = 0;
    if (IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOUSBHostDevice"), &iterator) != KERN_SUCCESS) return 0;
    io_service_t d;
    while ((d = IOIteratorNext(iterator))) {
        int vendor = property(d, CFSTR("idVendor")), product = property(d, CFSTR("idProduct"));
        if (vendor == 0x076B && (product == 0x5427 || product == 0x5428)) {
            count++; char serial[128] = "";
            CFTypeRef v = IORegistryEntryCreateCFProperty(d, CFSTR("USB Serial Number"), kCFAllocatorDefault, 0);
            if (v) { if (CFGetTypeID(v) == CFStringGetTypeID()) CFStringGetCString(v, serial, sizeof(serial), kCFStringEncodingUTF8); CFRelease(v); }
            snprintf(fingerprint, capacity, "5427:%08X:%s", property(d, CFSTR("locationID")), serial);
        }
        IOObjectRelease(d);
    }
    IOObjectRelease(iterator);
    IOHIDManagerRef m = hid_manager(); CFSetRef devices = IOHIDManagerCopyDevices(m);
    if (devices) {
        CFIndex n = CFSetGetCount(devices); const void **values = calloc(n ? n : 1, sizeof(void *)); CFSetGetValues(devices, values);
        for (CFIndex i = 0; i < n; i++) { IOHIDDeviceRef h = (IOHIDDeviceRef)values[i]; if (!supported_hid(h)) continue;
            int page = integer(IOHIDDeviceGetProperty(h, CFSTR(kIOHIDPrimaryUsagePageKey)));
            int usage = integer(IOHIDDeviceGetProperty(h, CFSTR(kIOHIDPrimaryUsageKey)));
            if (page == 1 && usage == 6) *mode = 1; else if (*mode != 1) *mode = 2;
        }
        free(values); CFRelease(devices);
    }
    CFRelease(m); return count;
}
int32_t br_enable_ccid(const char *expectedFingerprint) {
    char fingerprint[256]; int mode;
    if (br_usb_info(fingerprint, sizeof(fingerprint), &mode) != 1 || strcmp(fingerprint, expectedFingerprint)) return BR_AMBIGUOUS;
    IOHIDManagerRef m = hid_manager(); CFSetRef devices = IOHIDManagerCopyDevices(m); int32_t result = kIOReturnNotFound;
    if (devices) {
        CFIndex n = CFSetGetCount(devices); const void **values = calloc(n ? n : 1, sizeof(void *)); CFSetGetValues(devices, values);
        for (CFIndex i = 0; i < n; i++) { IOHIDDeviceRef h = (IOHIDDeviceRef)values[i]; if (!supported_hid(h)) continue;
            result = IOHIDDeviceOpen(h, kIOHIDOptionsTypeNone);
            if (result == kIOReturnSuccess) {
                uint8_t report[] = {0xA5,0x5A}; result = IOHIDDeviceSetReport(h, kIOHIDReportTypeFeature, 0, report, sizeof(report));
                IOHIDDeviceClose(h, kIOHIDOptionsTypeNone); if (result == kIOReturnSuccess) break;
            }
        }
        free(values); CFRelease(devices);
    }
    CFRelease(m); return result;
}

#include <mach/mach.h>
uint64_t br_memory_footprint(void) {
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return 0;
    return info.phys_footprint;
}
