using System.Runtime.InteropServices;
using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// Assembly kimliği. GUID sabittir; sürümle birlikte değişmez, aksi halde
// uygulamayı kimliğinden tanıyan dış araçlar (envanter, imzalama, kurulum
// kayıtları) her sürümü ayrı bir ürün sanır.
[assembly: Guid("BCDF96C0-CFE0-4AE9-926A-EE94A0A7F155")]

// Uygulama COM üzerinden kullanılmaz.
[assembly: ComVisible(false)]
