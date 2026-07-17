/*
 * skua_flash.h — C ABI for libskua_flash.so
 *
 * Layer 3b of the VibeSkua native-Linux port. This is the surface Skua's Linux
 * IFlashUtil [DllImport]s. Hand-kept in sync with native/skua-flash-bridge/src/ffi.rs;
 * see that file and README.md for semantics.
 *
 * Ownership / threading:
 *   - Strings returned by skua_flash_call() are owned by the caller and MUST be
 *     released with skua_flash_string_free().
 *   - The invoke_xml passed to the AS3->host callback is valid only for the
 *     duration of the callback; copy it out synchronously.
 *   - A bridge handle is not internally synchronised; serialise calls per handle
 *     (Skua already funnels Flash access through a single lock).
 */
#ifndef SKUA_FLASH_H
#define SKUA_FLASH_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* AS3 -> host callback: (user_data, invoke_xml_utf8). */
typedef void (*SkuaFlashCallback)(void *user_data, const char *invoke_xml);

/* Returns the ABI version implemented by this library (currently 1). */
uint32_t skua_flash_abi_version(void);

/* Create a bridge instance. Returns an opaque handle, or NULL on failure. */
void *skua_flash_create(void);

/* Destroy a bridge created by skua_flash_create(). NULL-safe. */
void skua_flash_destroy(void *handle);

/* Register the AS3 -> host event sink. user_data is passed back verbatim. */
void skua_flash_set_callback(void *handle, SkuaFlashCallback cb, void *user_data);

/* Load skua.swf bytes. Returns 0 on success, negative on error. */
int32_t skua_flash_load_swf(void *handle, const uint8_t *bytes, size_t len);

/*
 * Host -> AS3. invoke_xml is an ExternalInterface <invoke .../> string.
 * Returns a newly allocated, NUL-terminated UTF-8 value-XML string that the
 * caller must free with skua_flash_string_free(). Returns NULL on null args or
 * internal error.
 */
char *skua_flash_call(void *handle, const char *invoke_xml);

/* Free a string returned by skua_flash_call(). NULL-safe. */
void skua_flash_string_free(char *s);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* SKUA_FLASH_H */
