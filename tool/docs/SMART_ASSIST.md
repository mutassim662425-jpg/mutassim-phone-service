# Smart Assist — MUTASSIM TOOL PRO ⇄ MUTASSIM GSM CLOUD (V1.5)

## الفكرة / How it works

```
 MUTASSIM TOOL PRO                    MUTASSIM GSM CLOUD SERVER
 ─────────────────                    ─────────────────────────
 1. الفني يختار المشكلة  ──POST /api/assist/diagnose──►  محرك الحلول (SolutionEngine)
    + حالة الهاتف (الوضع، الماركة،                       يختار الخطوات حسب:
      الموديل، المعالج، أندرويد)                         المشكلة + الوضع + الماركة + المعالج
                             ◄── خطوات (ActionId فقط) + تعليمات ──
 2. الأداة تعرض الخطوات
 3. الفني يضغط "تنفيذ" ← الأداة تشغّل وظيفتها المدمجة على الهاتف
    (نفس فحص الوضع + نفس رسائل التأكيد + سجل العمليات)
 4. ──POST /api/assist/feedback── نتيجة كل خطوة ──►  تُحفظ في الجلسة
```

**The server never sends commands.** It sends only action IDs from a fixed list of 20. The tool maps each ID to
one of its own reviewed functions (`AssistActionMap`). Any other ID is dropped and shown as "ignored".
Both sides keep the same list. The tests check that the lists match and that every ID maps to a real function.

No serial number or IMEI is sent to the server.

## Problems the server can solve

| Code | المشكلة |
|---|---|
| identify | التعرف على الهاتف وقراءة معلوماته |
| not_detected | الهاتف غير معرّف على الكمبيوتر |
| unauthorized | تصحيح USB غير مصرّح |
| bootloop | معلّق على الشعار / ريستارت متكرر |
| stuck_fastboot | معلّق في Fastboot |
| stuck_recovery | معلّق في Recovery |
| flash_prepare | تجهيز لتفليش رسمي |
| app_problem | تطبيق يسبب مشاكل |
| account_locked | حساب Google بعد الفورمات (FRP): طريق المالك الرسمي فقط |
| managed_device | هاتف تابع لجهة (MDM): عن طريق مسؤول الجهة فقط |
| edl | وضع EDL لمعالجات Qualcomm فقط |

## Allowed actions (both sides)

`device.info, adb.info, fb.info, usb.scan, adb.reboot.{system,recovery,bootloader,fastbootd,download,edl},
fb.reboot.{system,recovery,bootloader,fastbootd}, adb.apps.user, adb.uninstall, sam.fw.check, frp.info, frp.google, mdm.info`

`adb.reboot.edl` and `adb.uninstall` always ask for confirmation and are never run by "Run safe steps".
There is no bypass, IMEI, erase, flash or unlock action.

## Use

1. Start the server: in `MUTASSIM_GSM_CLOUD` run `.\tools\start-server.ps1`.
2. Open MUTASSIM TOOL PRO → title-bar button **Smart Assist (Server)** (cloud icon).
3. Server address: `http://localhost:5179` → **Save & connect**. A green dot means connected.
4. Connect the phone, pick the problem → **Get solution from server**.
5. Press **Run** on each step, or **Run safe steps**.

If the server runs with an API key, set the same key for the tool before you open it:
`$env:MUTASSIM_API_KEY = "your-key"`, then start the tool from that PowerShell window.

For a server on another PC, use its LAN address (e.g. `http://192.168.1.20:5179`). Use `https://` over the internet.

## Files

Tool:
- `src/MUTASSIM.ToolPro.Api/Assist/AssistContracts.cs`: JSON contract + allowed action list
- `src/MUTASSIM.ToolPro.Api/Assist/AssistClient.cs`: HTTP client, drops unknown actions, readable errors
- `src/MUTASSIM.ToolPro.App/Workspace/AssistActionMap.cs`: action ID → built-in function
- `src/MUTASSIM.ToolPro.App/ViewModels/AssistViewModel.cs`, `Views/AssistView.xaml`: the page
- `Workspace/OperationRunner.cs`: `RunAsync` now returns the real result (for the feedback)
- `Core/Configuration/AppSettings.cs`: `AssistServerUrl`, `AssistClientId`
- `tests/.../AssistClientTests.cs`: 14 tests

Server (MUTASSIM_GSM_CLOUD V1.6):
- `MUTASSIM_SERVER.Core/Assist/*`: models, allowed actions, `SolutionEngine`, session store
- `MUTASSIM_SERVER/Program.cs`: `/api/assist/problems | actions | diagnose | feedback | sessions`
- `MUTASSIM_SERVER.Tests`: 1,396 tests (every problem × mode × brand × chipset)
