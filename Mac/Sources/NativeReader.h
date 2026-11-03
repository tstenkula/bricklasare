#ifndef BRICKLASARE_NATIVE_READER_H
#define BRICKLASARE_NATIVE_READER_H
#include <stdint.h>
#include <stddef.h>
typedef struct BRReader BRReader;
// All objects are owned by the caller and confined to one serial worker queue.
int32_t br_open(BRReader **result);
void br_close(BRReader *reader);
// Input is at most 262 bytes; output capacity is 1–4096 bytes. On failure outputLength is zero.
int32_t br_exchange(BRReader *reader, const uint8_t *input, uint32_t length, uint8_t *output, uint32_t *outputLength);
int32_t br_presence(BRReader *reader, int *present);
const char *br_name(BRReader *reader);
// Returns physical supported USB device count, fingerprint and active HID mode.
// Returns -1 on invalid arguments or enumeration failure; callers must fail closed.
// mode: 0 = no HID interface (CCID), 1 = keyboard, 2 = custom HID.
int br_usb_info(char *fingerprint, size_t capacity, int *mode);
int32_t br_enable_ccid(const char *expectedFingerprint);
// Resident application footprint, used only by offline lifecycle tests.
uint64_t br_memory_footprint(void);

#endif
