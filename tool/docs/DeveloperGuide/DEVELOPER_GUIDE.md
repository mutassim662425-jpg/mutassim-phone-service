# دليل المطوّر — MUTASSIM TOOL PRO

## المتطلبات
- Windows 10/11 x64
- Visual Studio 2026 (أو 2022 17.14+) مع حمولة **.NET desktop development**
- .NET SDK 10.0.100 أو أحدث

## البناء والتشغيل
```powershell
dotnet restore
dotnet build -c Debug
dotnet test tests/MUTASSIM.ToolPro.Tests.Unit
dotnet run --project src/MUTASSIM.ToolPro.App
```
أو افتح `MUTASSIM_TOOL_PRO.sln` واضغط F5 (مشروع البدء: `MUTASSIM.ToolPro.App`). أو شغّل `build.ps1`.

مجلد البيانات: `%LOCALAPPDATA%\MUTASSIM\ToolPro` (logs, data\toolpro.db, plugins, settings.json).

## إضافة ماركة جديدة (داخل المشروع)
1. أنشئ صنفاً يرث `ServiceModuleBase`:
```csharp
public sealed class AcmeModule : ServiceModuleBase
{
    public override string Id => "acme";
    public override string DisplayName => "Acme";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description => "...";
    public override IReadOnlyList<string> Brands => ["acme"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x1234];
    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Svc.Planned("acme.info", "Device information", 6) with { RequiredModes = [ConnectionMode.Adb] }
    ];
}
```
2. سجّله في `ServiceRegistration.AddToolPro`: `services.AddSingleton<IServiceModule, AcmeModule>();`
3. أضف عنصر القائمة في `Navigation/Routes.cs` (`Routes.Module("acme")`) ومفتاح الترجمة في `tools/gen_strings.py` ثم شغّله: `python tools/gen_strings.py src/MUTASSIM.ToolPro.App/Localization`.

## إضافة ماركة كإضافة (Plugin) — بدون إعادة بناء البرنامج
1. مشروع Class Library يستهدف `net10.0` ويشير إلى `MUTASSIM.ToolPro.Core` (مع `<Private>false</Private>` و `<ExcludeAssets>runtime</ExcludeAssets>`).
2. صنف عام ينفذ `IToolPlugin`:
```csharp
public sealed class AcmePlugin : IToolPlugin
{
    public void Register(IModuleRegistry registry) => registry.Register(new AcmeModule());
}
```
3. ضع الناتج في `%LOCALAPPDATA%\MUTASSIM\ToolPro\plugins\Acme\` مع `plugin.json`:
```json
{ "id": "acme", "name": "Acme Plugin", "version": "1.0.0", "vendor": "MUTASSIM SOFTWARE",
  "hostApiVersion": 1, "entryAssembly": "Acme.Plugin.dll",
  "supportedDevices": ["Acme X1"], "services": ["Device information"], "requirements": ["ADB"] }
```
4. حتى المرحلة 8 (التحقق من التوقيع) لا تُحمَّل الإضافات غير الموقعة إلا بتفعيل «Allow unsigned plugins» في الإعدادات.

## إضافة محرك اكتشاف (المرحلة 2)
نفّذ `IDeviceDetector` وسجّله: `services.AddSingleton<IDeviceDetector, AdbDeviceDetector>();` — `DeviceManagerService` يلتقطه تلقائياً. القواعد:
- `Key` فريد لكل اتصال فعلي (مثلاً `adb:<serial>` أو `usb:<instance-id>`).
- لا تملأ أي حقل بقيمة مُخمَّنة؛ اتركه `null`.
- لا ترمِ استثناء عند عدم وجود أجهزة؛ أرجع قائمة فارغة.

## تنفيذ خدمة (عملية)
```csharp
await using var op = await tracker.BeginAsync("adb.reboot", "ADB reboot", device);
var result = await adb.RebootAsync(device, ct);
if (result.ExitCode == 0) await op.SucceedAsync("Rebooted");
else await op.FailAsync($"adb exit {result.ExitCode}: {result.StdErr}");
```
عند الخروج بدون `SucceedAsync/FailAsync` تُسجَّل العملية «فشلت» تلقائياً.
عند اكتمال الخدمة وتجربتها على جهاز حقيقي غيّر حالتها إلى `CompletedTested` **واحذف اختبار الحماية** فقط بعد توثيق الاختبار.

## الترجمة
ملفات `src/MUTASSIM.ToolPro.App/Localization/Strings.en.xaml` و `Strings.ar.xaml` تحتوي نفس المفاتيح (219 مفتاحاً). في XAML: `{DynamicResource Key}`، في الكود: `LocalizationService.Get("Key")`. نصوص الحالات: مفتاح `EnumType.Value`.
