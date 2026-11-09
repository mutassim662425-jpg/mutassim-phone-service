# جدول حالة الوظائف — MUTASSIM TOOL PRO

> مُولَّد من كود الوحدات، ونفس الجدول حي داخل البرنامج (About).

| الحالة | العدد |
|---|---|
| مكتملة وتحتاج اختباراً على جهاز حقيقي | 66 |
| قيد التطوير | 34 |
| تحتاج صلاحية أو حساباً خارجياً | 7 |
| تحتاج بروتوكولاً أو عتاداً إضافياً | 4 |
| غير مدعومة (بالتصميم) | 3 |

| الوحدة | الخدمة | الحالة | المرحلة |
|---|---|---|---|
| ADB Services | Detect ADB devices | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| ADB Services | Read device information (getprop) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| ADB Services | List installed packages | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Install APK | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Uninstall user app | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Reboot | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Reboot to Recovery | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Reboot to Bootloader | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Reboot to Download Mode | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | ADB Terminal (validated commands) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| ADB Services | Command history & saved results | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| API Services | Add / remove providers (plugins) | قيد التطوير | 7 |
| API Services | Secure API key storage (DPAPI) | قيد التطوير | 7 |
| API Services | Service catalogue & prices | قيد التطوير | 7 |
| API Services | Place order / track status / receive result | قيد التطوير | 7 |
| API Services | Balance (when provider supports it) | قيد التطوير | 7 |
| API Services | Timeouts, retries, provider error display | قيد التطوير | 7 |
| Apple (iPhone / iPad) | Identify Normal / Recovery / DFU by VID:PID | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| Apple (iPhone / iPad) | Device name, model, iOS version | قيد التطوير | 6 |
| Apple (iPhone / iPad) | USB diagnostics & Apple driver status | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Apple (iPhone / iPad) | Firmware (IPSW) information & signing status | قيد التطوير | 6 |
| Apple (iPhone / iPad) | Official restore / update | تحتاج بروتوكولاً أو عتاداً إضافياً | 6 |
| Apple (iPhone / iPad) | Activation Lock / passcode bypass | غير مدعومة (بالتصميم) | — |
| FRP Services | Detect whether account verification is requested (when exposed) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| FRP Services | Show model and OS version | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| FRP Services | Diagnose account-access problem | قيد التطوير | 6 |
| FRP Services | Google account recovery guidance for the owner | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| FRP Services | Manufacturer official procedure & proof-of-ownership checklist | قيد التطوير | 6 |
| FRP Services | Service request tracking | قيد التطوير | 6 |
| FRP Services | Unauthorised FRP bypass | غير مدعومة (بالتصميم) | — |
| MDM Services | Detect device-owner / work-profile management (ADB, when permitted) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| MDM Services | Show management status & organisation details | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| MDM Services | Diagnose enrolment problems | قيد التطوير | 6 |
| MDM Services | De-enrolment request workflow | قيد التطوير | 6 |
| MDM Services | Release / de-enrolment | تحتاج صلاحية أو حساباً خارجياً | 6 |
| MDM Services | Unauthorised MDM bypass | غير مدعومة (بالتصميم) | — |
| Fastboot Services | Detect Fastboot devices | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| Fastboot Services | Read variables (getvar all) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Fastboot Services | Read product name | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Fastboot Services | Read bootloader lock state | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Fastboot Services | Reboot to system | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Fastboot Services | Reboot to Recovery | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Fastboot Services | List partitions (as exposed by device) | قيد التطوير | 3 |
| Fastboot Services | Firmware compatibility check | قيد التطوير | 3 |
| Fastboot Services | Flash official images | قيد التطوير | 3 |
| Fastboot Services | Bootloader unlock (OEM official method) | تحتاج صلاحية أو حساباً خارجياً | 3 |
| Firmware Manager | SHA-256 integrity check (with expected-hash verification) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| Firmware Manager | Add files by Drag & Drop / Browse | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| Firmware Manager | Library organised by brand / model / version | قيد التطوير | 4 |
| Firmware Manager | Search by model and OS version | قيد التطوير | 4 |
| Firmware Manager | Metadata extraction (Samsung BL/AP/CP/CSC/HOME_CSC tar.md5) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Firmware Manager | Missing-file detection | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Firmware Manager | Compatibility check against connected device | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Firmware Manager | Per-firmware usage history | قيد التطوير | 4 |
| Firmware Manager | Import / export firmware lists | قيد التطوير | 4 |
| Honor | Device information (model, system version) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Honor | USB diagnostics | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Honor | ADB tools (where permitted) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Honor | Fastboot tools (where supported) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Honor | Firmware files & compatibility | قيد التطوير | 6 |
| Honor | System recovery assistance | تحتاج بروتوكولاً أو عتاداً إضافياً | 6 |
| Honor | Authorised service functions | تحتاج صلاحية أو حساباً خارجياً | 6 |
| Huawei | Device information (model, system version) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Huawei | USB diagnostics | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Huawei | ADB tools (where permitted) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Huawei | Fastboot tools (where supported) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Huawei | Firmware files & compatibility | قيد التطوير | 6 |
| Huawei | System recovery assistance | تحتاج بروتوكولاً أو عتاداً إضافياً | 6 |
| Huawei | Authorised service functions | تحتاج صلاحية أو حساباً خارجياً | 6 |
| MediaTek | Identify Preloader / BROM by VID:PID | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| MediaTek | USB information & connection type | مكتملة وتحتاج اختباراً على جهاز حقيقي | 5 |
| MediaTek | Chipset information (when reported) | قيد التطوير | 5 |
| MediaTek | MediaTek driver management | مكتملة وتحتاج اختباراً على جهاز حقيقي | 5 |
| MediaTek | Connection diagnostics | قيد التطوير | 5 |
| MediaTek | Firmware (scatter) verification & compatibility | قيد التطوير | 5 |
| MediaTek | Officially supported system restore | تحتاج صلاحية أو حساباً خارجياً | 5 |
| Brand profiles ×13 | Device detection | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | Device information | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | ADB services | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | Fastboot services (when supported) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | Firmware information | قيد التطوير | 6 |
| Brand profiles ×13 | Firmware compatibility | قيد التطوير | 6 |
| Brand profiles ×13 | USB diagnostics & drivers | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | Reboot services | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Brand profiles ×13 | Supported official flashing | قيد التطوير | 6 |
| Brand profiles ×13 | Operation history | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Qualcomm | Identify EDL 9008 / Diag by VID:PID | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| Qualcomm | COM port & driver status | مكتملة وتحتاج اختباراً على جهاز حقيقي | 5 |
| Qualcomm | USB link health check | قيد التطوير | 5 |
| Qualcomm | Model identification (when the interface reports it) | قيد التطوير | 5 |
| Qualcomm | Show required authorised files per device | قيد التطوير | 5 |
| Qualcomm | Firehose-based service operations | تحتاج صلاحية أو حساباً خارجياً | 5 |
| Samsung | Model / CSC / Firmware / Binary / Security patch | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Samsung | Download Mode detection | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Samsung | Download Mode connection details | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Samsung | Firmware set analysis BL/AP/CP/CSC/HOME_CSC | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Samsung | Firmware integrity (tar.md5) & model match | مكتملة وتحتاج اختباراً على جهاز حقيقي | 4 |
| Samsung | Reboot to Download Mode (ADB) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 3 |
| Samsung | Firmware flashing | تحتاج بروتوكولاً أو عتاداً إضافياً | 4 |
| USB Drivers | VID/PID classification (known vendor & mode IDs) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 1 |
| USB Drivers | Live USB device enumeration & hot-plug events | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| USB Drivers | Detect unknown / driver-less devices | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| USB Drivers | Installed / missing driver status | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| USB Drivers | COM port mapping for serial devices | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| USB Drivers | Official driver sources per vendor | قيد التطوير | 5 |
| USB Drivers | Rescan after driver installation | مكتملة وتحتاج اختباراً على جهاز حقيقي | 2 |
| Xiaomi / Redmi / POCO | ADB information (model, build, MIUI/HyperOS version) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Xiaomi / Redmi / POCO | Fastboot information (product, lock state) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Xiaomi / Redmi / POCO | Chipset family detection (Qualcomm / MediaTek) | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Xiaomi / Redmi / POCO | ROM compatibility check (codename / region / anti-rollback) | قيد التطوير | 6 |
| Xiaomi / Redmi / POCO | Reboot services | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Xiaomi / Redmi / POCO | Connection & driver diagnostics | مكتملة وتحتاج اختباراً على جهاز حقيقي | 6 |
| Xiaomi / Redmi / POCO | Official fastboot ROM flashing | قيد التطوير | 6 |
| Xiaomi / Redmi / POCO | Bootloader unlock | تحتاج صلاحية أو حساباً خارجياً | 6 |
