# MUTASSIM TOOL PRO V1.0 — التحليل والمعمارية

## 1. تحليل المشروع

المشروع منصة صيانة GSM متعددة الماركات. التحدي الأساسي ليس الواجهة بل **محركات الاتصال**: كل ماركة/معالج/وضع اتصال له بروتوكول مختلف، وبعض البروتوكولات مغلقة أو تتطلب توقيعاً رسمياً. لذلك صُمم النظام حول ثلاث أفكار:

1. **فصل صارم**: الواجهة (WPF) لا تعرف شيئاً عن USB أو ADB؛ تتعامل فقط مع واجهات (`interfaces`) في `Core`.
2. **الوحدات (Modules) قابلة للإضافة**: كل ماركة أو معالج أو بروتوكول هو `IServiceModule` يُسجَّل في `IModuleRegistry` — داخلياً أو كإضافة Plugin.
3. **الصدق في الحالة**: كل خدمة تعلن حالتها الحقيقية (`ImplementationStatus`) والمرحلة التي ستُنفذ فيها. لا يوجد زر لخدمة غير منفذة، ولا تُسجَّل عملية ناجحة إلا بنداء صريح `SucceedAsync` (التخلص من العملية بدون نتيجة = فشل).

## 2. المعمارية

```
┌──────────────────────────── MUTASSIM.ToolPro.App (WPF, MVVM) ────────────────────────────┐
│ Views (XAML) ◄─ DataTemplates ─► ViewModels ─► NavigationService (scope لكل صفحة)          │
│ LocalizationService (AR/EN + RTL)   DialogService   Composition Root (DI)                  │
└───────────────▲─────────────────────────────────────────────────────────────────────────┘
                │ يعتمد على الواجهات فقط
┌───────────────┴──────────────── MUTASSIM.ToolPro.Core ─────────────────────────────────────┐
│ DeviceInfo, ConnectionMode, IDeviceDetector, IDeviceManager                                │
│ IServiceModule, ServiceDescriptor, ImplementationStatus, IModuleRegistry                   │
│ IOperationTracker, IOperationRepository, ISettingsService, IConnectivityService            │
│ ILicenseService, IUpdateService, IExternalToolLocator, IToolPlugin (Plugin API v1)         │
└───────▲──────────────▲───────────────▲───────────────▲──────────────▲──────────────────────┘
        │              │               │               │              │
 Infrastructure   DeviceManager     Database        Logging       Plugins / Updates / Licensing
 (settings, http, (محرك مركزي،      (SQLite +      (Serilog +     (تحميل معزول، فحص الإصدارات،
  tool locator,    مقارنة الأجهزة،   migrations)    ذاكرة للطرفية)  الترخيص من السيرفر)
  registry,        أحداث توصيل/فصل)
  operation tracker)
        ▲
 وحدات الأجهزة: Usb · Adb · Fastboot · Firmware · Samsung · Xiaomi · Huawei · Honor ·
               Qualcomm · MediaTek · Apple · MultiBrand (13 ماركة) · Assistance (FRP/MDM) · Api
```

### تدفق اكتشاف جهاز (من المرحلة الثانية)
`IDeviceDetector` (USB/WMI، ADB، Fastboot، Apple) ← `DeviceManagerService` يدمج النتائج حسب `DeviceInfo.Key` ← حدث `Connected/Updated/Disconnected` ← `IModuleRegistry.ForDevice()` يحدد الوحدات المتوافقة ← الواجهة تعرض البطاقة وزر الخدمات المتوافقة.

ضمانات مبنية في المحرك (ومختبرة):
- جهازان مختلفان لا يُدمجان أبداً (المفتاح فريد لكل اتصال).
- فشل محرك اكتشاف لا يُظهر «فصل» وهمياً — تبقى آخر نتيجة حقيقية ويُسجَّل الخطأ.
- كل محرك له مهلة 10 ثوانٍ حتى لا يُجمِّد الاكتشاف.

### المبادئ
| المبدأ | التطبيق |
|---|---|
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) |
| DI | Microsoft.Extensions.DependencyInjection مع `ValidateOnBuild` |
| Async | كل I/O غير متزامن؛ الأحداث من الخلفية تُمرَّر للواجهة عبر `Ui.Post` |
| Plugins | `AssemblyLoadContext` لكل إضافة + `plugin.json` + رقم إصدار API |
| السجلات | Serilog: ملف يومي (30 يوماً) + طرفية داخلية |
| الخصوصية | لا يُخزَّن الرقم التسلسلي كنص؛ يُحفظ بصمة SHA-256 مختصرة فقط |

## 3. المكتبات (كلها تسمح بالتوزيع التجاري)

| المكتبة | الترخيص | الاستخدام |
|---|---|---|
| .NET 10 (LTS) + WPF | MIT | المنصة والواجهة |
| CommunityToolkit.Mvvm 8.4 | MIT | MVVM |
| Microsoft.Extensions.Hosting/DI/Logging 10 | MIT | حقن الاعتماديات والسجلات |
| Microsoft.Data.Sqlite 10 | MIT (SQLite: Public Domain) | قاعدة البيانات المحلية |
| Serilog 4 + Sinks.File | Apache-2.0 | تسجيل العمليات |
| xUnit | Apache-2.0 | الاختبارات |
| System.Text.Json, HttpClient, SHA-256 | مدمجة في .NET | JSON، الشبكة، التحقق |

مخطط لمراحل لاحقة (يُقيَّم قبل الإضافة): `System.Management` (MIT) لـ WMI في المرحلة 2، و `System.IO.Ports` (MIT) لمنافذ COM في المرحلة 5، و `System.Security.Cryptography.ProtectedData` (MIT) لتشفير مفاتيح API في المرحلة 7.

## 4. متطلبات تحتاج أدوات أو صلاحيات خارجية

| المتطلب | لماذا | الحالة |
|---|---|---|
| Google Android SDK Platform-Tools (adb/fastboot) | البروتوكول الرسمي لـ ADB/Fastboot. رخصة Google تسمح بالاستخدام؛ **إعادة توزيعها داخل المثبّت تحتاج مراجعة الرخصة** — البديل: يحددها المستخدم في الإعدادات | مدعوم الآن (البحث عنها) |
| تعريفات USB الرسمية (Samsung, Qualcomm, MediaTek, Apple) | الاتصال بأوضاع Download/EDL/BROM | يتم توجيه المستخدم لمصادرها الرسمية |
| بروتوكول Samsung Download Mode | مغلق؛ يحتاج تنفيذاً موثوقاً قابلاً للتوزيع قانونياً | مُعلَن «يحتاج بروتوكولاً» |
| ملفات Firehose/DA موقعة من الشركة | Qualcomm EDL و MediaTek BROM على الأجهزة المحمية | «تحتاج صلاحية» — لا تُستخدم ثغرات |
| Mi Unlock / حسابات الخدمة المعتمدة (Huawei/Honor) | فتح Bootloader والخدمات الخاصة | عبر الإجراءات الرسمية فقط |
| Apple Mobile Device Support | الاتصال بأجهزة iPhone | يُثبَّت مع تطبيق Apple Devices / iTunes |
| شهادة توقيع كود (Code Signing) | توقيع البرنامج والتحديثات والإضافات | مطلوبة قبل الإصدار التجاري |

## 5. ما يعمل محلياً وما يحتاج سيرفر

| محلي بالكامل | يحتاج سيرفر MUTASSIM |
|---|---|
| اكتشاف الأجهزة وقراءة المعلومات | التحقق من الترخيص وربطه بالجهاز (المرحلة 8) |
| ADB / Fastboot / التفليش الرسمي | ملف التحديثات + الملفات الموقعة (المرحلة 8) |
| مدير الفلاشات والتحقق SHA-256 | حالة خدمات السيرفر (الشريط العلوي) |
| سجل العمليات (SQLite) والطرفية | مزودو خدمات API (طرف ثالث، المرحلة 7) |
| الإعدادات واللغة والإضافات | نشر الإضافات الموقعة (اختياري) |

**مهم:** التحقق الحقيقي من الترخيص يجب أن يكون في السيرفر (توكن موقّع بالمفتاح الخاص للسيرفر، مربوط ببصمة الجهاز، مع فترة سماح عند انقطاع الإنترنت). لذلك لا يحتوي البرنامج على أي مقارنة لمفتاح ثابت.

## 6. هيكل الحل

```
MUTASSIM_TOOL_PRO/
├─ MUTASSIM_TOOL_PRO.sln          (24 مشروعاً)
├─ Directory.Build.props          (الإصدار، الشركة، إعدادات C#)
├─ Directory.Packages.props       (إصدارات الحزم مركزياً)
├─ src/
│  ├─ MUTASSIM.ToolPro.App            WPF — الواجهة فقط
│  ├─ MUTASSIM.ToolPro.Core           العقود (لا UI، لا I/O)
│  ├─ MUTASSIM.ToolPro.Infrastructure إعدادات، شبكة، سجل الوحدات، متتبع العمليات
│  ├─ MUTASSIM.ToolPro.DeviceManager  المحرك المركزي للاكتشاف
│  ├─ MUTASSIM.ToolPro.Database       SQLite + ترحيل المخطط
│  ├─ MUTASSIM.ToolPro.Logging        Serilog + طرفية
│  ├─ MUTASSIM.ToolPro.Usb/Adb/Fastboot/Firmware
│  ├─ MUTASSIM.ToolPro.Samsung/Xiaomi/Huawei/Honor/Apple
│  ├─ MUTASSIM.ToolPro.Qualcomm/MediaTek
│  ├─ MUTASSIM.ToolPro.MultiBrand     OPPO, Realme, Vivo, Tecno, Infinix, Motorola, Nokia, Pixel, OnePlus, ZTE, Lenovo, Sony, LG
│  ├─ MUTASSIM.ToolPro.Assistance     FRP / MDM (إرشاد المالك فقط)
│  ├─ MUTASSIM.ToolPro.Api            عقود مزودي الخدمات
│  └─ MUTASSIM.ToolPro.Licensing/Updates/Plugins
├─ tests/MUTASSIM.ToolPro.Tests.Unit
├─ docs/ (Architecture, DeveloperGuide, UserGuide, FEATURE_STATUS.md)
├─ installer/   resources/
```

إضافات على الهيكل المقترح: `MultiBrand` (نظام موحد لماركات القسم 10 بدل 13 مشروعاً متشابهاً) و `Assistance` (FRP/MDM).
