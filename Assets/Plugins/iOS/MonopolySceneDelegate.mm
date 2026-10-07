// UIScene lifecycle support for Unity's iOS app shell.
//
// iOS 27 refuses to launch apps that don't adopt the UIScene lifecycle. Unity 6000.2's generated app only
// implements the app-delegate lifecycle, so this scene delegate (registered in Info.plist by IOSPostBuild.cs)
// forwards scene events to UnityAppController's existing handlers. Remove it once Unity's own template
// declares a scene configuration.

#import <UIKit/UIKit.h>
#import "UnityAppController.h"

@interface MonopolySceneDelegate : UIResponder <UIWindowSceneDelegate>
@end

@implementation MonopolySceneDelegate

- (UIWindow*)window
{
    return GetAppController().window;
}

- (void)setWindow:(UIWindow*)window
{
    // Unity owns its window.
}

- (void)scene:(UIScene*)scene willConnectToSession:(UISceneSession*)session options:(UISceneConnectionOptions*)connectionOptions
{
    if (![scene isKindOfClass: [UIWindowScene class]])
        return;

    // If Unity already initialised during didFinishLaunching (before any scene existed), its window has no
    // scene yet: attach it to this one. Otherwise Unity starts in sceneDidBecomeActive below.
    UIWindow* window = GetAppController().window;
    if (window != nil && window.windowScene == nil)
    {
        window.windowScene = (UIWindowScene*)scene;
        [window makeKeyAndVisible];
    }
}

- (void)sceneDidBecomeActive:(UIScene*)scene
{
    [GetAppController() applicationDidBecomeActive: [UIApplication sharedApplication]];
}

- (void)sceneWillResignActive:(UIScene*)scene
{
    [GetAppController() applicationWillResignActive: [UIApplication sharedApplication]];
}

- (void)sceneWillEnterForeground:(UIScene*)scene
{
    [GetAppController() applicationWillEnterForeground: [UIApplication sharedApplication]];
}

- (void)sceneDidEnterBackground:(UIScene*)scene
{
    [GetAppController() applicationDidEnterBackground: [UIApplication sharedApplication]];
}

@end
