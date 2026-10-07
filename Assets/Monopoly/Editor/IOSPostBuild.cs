#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Monopoly.Editor
{
    /// <summary>Info.plist entries Unity doesn't add on its own.</summary>
    public static class IOSPostBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            // LAN games connect directly to another device on the network, which iOS asks permission for.
            root.SetString("NSLocalNetworkUsageDescription", "Monopoly connects to friends' games on your local network.");
            // No encryption beyond standard HTTPS, so App Store Connect won't ask on every upload.
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            // iOS 27 requires the UIScene lifecycle; see Assets/Plugins/iOS/MonopolySceneDelegate.mm.
            var manifest = root.CreateDict("UIApplicationSceneManifest");
            manifest.SetBoolean("UIApplicationSupportsMultipleScenes", false);
            var config = manifest.CreateDict("UISceneConfigurations").CreateArray("UIWindowSceneSessionRoleApplication").AddDict();
            config.SetString("UISceneConfigurationName", "Default Configuration");
            config.SetString("UISceneDelegateClassName", "MonopolySceneDelegate");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
