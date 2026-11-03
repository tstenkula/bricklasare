// Offline native boundary tests: PC/SC calls are replaced before compiling the production source.
#include <PCSC/winscard.h>
#include <IOKit/IOKitLib.h>
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint32_t reported_length;
static int32_t control_status;
static unsigned control_calls, context_releases, hardware_calls, assertions;
static const char *mock_names;
static uint32_t names_capacity, names_reported;
static int32_t mock_control(SCARDHANDLE card, uint32_t code, const void *input, uint32_t input_length,
                           void *output, uint32_t capacity, uint32_t *received) {
    (void)card; (void)code; (void)input; (void)input_length;
    control_calls++;
    if (capacity >= 2) { ((uint8_t *)output)[0] = 0x90; ((uint8_t *)output)[1] = 0; }
    *received = reported_length;
    return control_status;
}
static int32_t mock_establish(uint32_t scope, const void *a, const void *b, LPSCARDCONTEXT context) {
    (void)scope; (void)a; (void)b; *context = 1; return SCARD_S_SUCCESS;
}
static int32_t mock_release(SCARDCONTEXT context) { assert(context == 1); context_releases++; return 0; }
static int32_t mock_list(SCARDCONTEXT context, const char *groups, char *names, uint32_t *length) {
    (void)context; (void)groups;
    if (!names) { *length = names_capacity; return 0; }
    assert(*length == names_capacity);
    if (mock_names) memcpy(names, mock_names, names_capacity);
    *length = names_reported;
    return 0;
}
static kern_return_t mock_matching(mach_port_t port, CFDictionaryRef matching, io_iterator_t *iterator) {
    (void)port; (void)iterator; CFRelease(matching); hardware_calls++; return KERN_FAILURE;
}
#undef SCardControl
#define SCardControl mock_control
#define SCardEstablishContext mock_establish
#define SCardReleaseContext mock_release
#define SCardListReaders mock_list
#define IOServiceGetMatchingServices mock_matching
#include "../Sources/NativeReader.c"
#define CHECK(value) do { assertions++; if (!(value)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #value); exit(1); } } while (0)

static void test_exchange(void) {
    BRReader reader = {.card = 1, .control = BR_CTL(3500)};
    uint8_t output[4096] = {0}, input[1] = {0};
    uint32_t length;
    const uint32_t reports[] = {0, 1, 2, 511, 512, 513, 4096, UINT32_MAX};
    for (size_t i = 0; i < sizeof(reports) / sizeof(reports[0]); i++) {
        reported_length = reports[i]; control_status = 0; length = 512;
        int32_t status = exchange_code(&reader, reader.control, input, 1, output, &length);
        CHECK((status == 0) == (reported_length <= 512));
        CHECK(length == (reported_length <= 512 ? reported_length : 0));
    }
    control_status = SCARD_E_INSUFFICIENT_BUFFER; reported_length = UINT32_MAX; length = 512;
    CHECK(exchange_code(&reader, reader.control, input, 1, output, &length) == (int32_t)SCARD_E_INSUFFICIENT_BUFFER);
    CHECK(length == 0);
    unsigned before = control_calls;
    length = 512; CHECK(exchange_code(NULL, reader.control, input, 1, output, &length) != 0 && length == 0);
    length = 512; CHECK(exchange_code(&reader, reader.control, NULL, 1, output, &length) != 0 && length == 0);
    length = 512; CHECK(exchange_code(&reader, reader.control, input, 263, output, &length) != 0 && length == 0);
    length = 512; CHECK(exchange_code(&reader, reader.control, input, 1, NULL, &length) != 0 && length == 0);
    length = 0; CHECK(exchange_code(&reader, reader.control, input, 1, output, &length) != 0 && length == 0);
    length = 4097; CHECK(exchange_code(&reader, reader.control, input, 1, output, &length) != 0 && length == 0);
    CHECK(exchange_code(&reader, reader.control, input, 1, output, NULL) != 0);
    length = 512; CHECK(br_exchange(NULL, input, 1, output, &length) != 0 && length == 0);
    CHECK(control_calls == before);
    CHECK(br_presence(&reader, NULL) != 0);
    int present = 9; CHECK(br_presence(NULL, &present) != 0 && present == 0);
    CHECK(br_open(NULL) != 0);
    int mode = 9; char fingerprint[2];
    CHECK(br_usb_info(NULL, 2, &mode) == -1);
    CHECK(br_usb_info(fingerprint, 0, &mode) == -1);
    CHECK(br_usb_info(fingerprint, 2, NULL) == -1);
    CHECK(br_enable_ccid(NULL) != 0);
    CHECK(br_enable_ccid("") != 0);
}
static void test_names(void) {
    char selected[1024];
    const char good[] = "Unrelated reader\0OMNIKEY 5427 CK 00\0OMNIKEY 5427 CK CL 0\0";
    CHECK(select_reader(good, sizeof(good), selected, sizeof(selected)) == 0);
    CHECK(strcmp(selected, "OMNIKEY 5427 CK CL 0") == 0);
    CHECK(select_reader("\0", 2, selected, sizeof(selected)) == (int32_t)SCARD_E_NO_READERS_AVAILABLE);
    CHECK(select_reader("x\0", 3, selected, sizeof(selected)) == (int32_t)SCARD_E_NO_READERS_AVAILABLE);
    CHECK(select_reader(NULL, 0, selected, sizeof(selected)) != 0);
    CHECK(select_reader(good, sizeof(good) - 1, selected, sizeof(selected)) != 0);
    const char hidden[] = "OMNIKEY 5427\0\0concealed\0";
    CHECK(select_reader(hidden, sizeof(hidden), selected, sizeof(selected)) != 0 && selected[0] == 0);
    char large[1030]; memset(large, 'A', sizeof(large)); memcpy(large, "OMNIKEY 5427", 12);
    large[sizeof(large) - 2] = large[sizeof(large) - 1] = 0;
    CHECK(select_reader(large, sizeof(large), selected, sizeof(selected)) != 0 && selected[0] == 0);
    // A second PC/SC enumeration must not enlarge the allocated boundary or hide truncation.
    const char invalid[] = {'A', 'B', 'C', 'D'};
    mock_names = invalid; names_capacity = sizeof(invalid);
    const uint32_t lengths[] = {0, 1, 2, 4, 5, UINT32_MAX};
    for (size_t i = 0; i < sizeof(lengths) / sizeof(lengths[0]); i++) {
        names_reported = lengths[i]; BRReader *reader = (BRReader *)(uintptr_t)1;
        unsigned releases = context_releases;
        CHECK(br_open(&reader) != 0 && reader == NULL);
        CHECK(context_releases == releases + 1);
        CHECK(hardware_calls == 0);
    }
}
static void test_features(void) {
    uint32_t control;
    const uint8_t valid[] = {0x13, 4, 0x42, 0, 0x0D, 0xAC};
    CHECK(escape_feature(valid, sizeof(valid), &control) == 0 && control == BR_CTL(3500));
    CHECK(escape_feature(NULL, 0, &control) == 0 && control == 0);
    for (size_t n = 1; n < sizeof(valid); n++) CHECK(escape_feature(valid, n, &control) != 0 && control == 0);
    uint8_t conflict[12]; memcpy(conflict, valid, 6); memcpy(conflict + 6, valid, 6); conflict[11]++;
    CHECK(escape_feature(conflict, sizeof(conflict), &control) != 0 && control == 0);
    uint8_t zero[] = {0x13, 4, 0, 0, 0, 0};
    CHECK(escape_feature(zero, sizeof(zero), &control) != 0 && control == 0);
    uint8_t wrong_size[] = {0x13, 1, 2};
    CHECK(escape_feature(wrong_size, sizeof(wrong_size), &control) != 0 && control == 0);
    uint8_t trailing[] = {0x13, 4, 0x42, 0, 0x0D, 0xAC, 1};
    CHECK(escape_feature(trailing, sizeof(trailing), &control) != 0 && control == 0);
}
static uint32_t random_value(void) {
    static uint32_t state = 0x9E3779B9u;
    state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state;
}
static void test_corpus(void) {
    // Exact-sized allocations let AddressSanitizer catch reads past truncated input.
    for (unsigned iteration = 0; iteration < 20000; iteration++) {
        size_t size = random_value() % 1025;
        char *bytes = malloc(size ? size : 1); CHECK(bytes != NULL);
        for (size_t i = 0; i < size; i++) bytes[i] = (char)random_value();
        if (size >= 2 && iteration % 2) bytes[size - 1] = bytes[size - 2] = 0;
        char selected[1024];
        int32_t status = select_reader(bytes, size, selected, sizeof(selected));
        CHECK(status != 0 || memchr(selected, 0, sizeof(selected)) != NULL);
        if (status != 0) CHECK(selected[0] == 0);
        free(bytes);
        size = random_value() % 514;
        uint8_t *features = malloc(size ? size : 1); CHECK(features != NULL);
        for (size_t i = 0; i < size; i++) features[i] = (uint8_t)random_value();
        uint32_t control = UINT32_MAX;
        status = escape_feature(features, size, &control);
        CHECK(status == 0 || control == 0);
        free(features);
    }
}
int main(void) {
    test_exchange(); test_names(); test_features(); test_corpus();
    CHECK(hardware_calls == 0);
    printf("PASS: %u native boundary assertions; 40000 deterministic reader-list/feature cases; no hardware accessed.\n", assertions);
    return 0;
}
