# Integration tests (Phase 2+)

Hardware-in-the-loop tests (real phone connected) are added with the device engines in Phase 2.
They will run only when the environment variable `MTP_DEVICE_TESTS=1` is set and will report "skipped"
(never "passed") when no device is attached.
