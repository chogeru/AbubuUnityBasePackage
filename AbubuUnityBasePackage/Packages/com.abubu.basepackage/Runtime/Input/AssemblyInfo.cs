// IL2CPP の Managed Stripping で、どこからも参照されないこのアセンブリが丸ごと削除されないようにする
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

// HapticsService.HandleFocusChanged などをテストから呼ぶ
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Abubu.Tests.Input")]
