#import <Cocoa/Cocoa.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>

@interface LauncherHost : NSObject <NSApplicationDelegate>
@property(strong) NSWindow *window;
@property(strong) NSTask *engine;
@property(strong) NSTextField *heading;
@property(strong) NSTextField *status;
@property(strong) NSTextField *error;
@property(strong) NSProgressIndicator *progress;
@property(strong) NSButton *zipButton;
@property(strong) NSButton *folderButton;
@property(strong) NSButton *launchButton;
@property(strong) NSURL *origin;
@property(copy) NSString *token;
@property(strong) NSTimer *timer;
@property BOOL polling;
@property BOOL requesting;
@end

@implementation LauncherHost
- (NSTextField *)label:(NSString *)text size:(CGFloat)size color:(NSColor *)color {
  NSTextField *label=[NSTextField wrappingLabelWithString:text];
  label.font=[NSFont systemFontOfSize:size];label.textColor=color;label.translatesAutoresizingMaskIntoConstraints=NO;
  return label;
}
- (NSButton *)button:(NSString *)title action:(SEL)action {
  NSButton *button=[NSButton buttonWithTitle:title target:self action:action];button.bezelStyle=NSBezelStyleRounded;
  button.controlSize=NSControlSizeLarge;button.font=[NSFont systemFontOfSize:15];button.translatesAutoresizingMaskIntoConstraints=NO;
  [button.heightAnchor constraintEqualToConstant:36].active=YES;return button;
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
  self.window=[[NSWindow alloc]initWithContentRect:NSMakeRect(0,0,520,410) styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskMiniaturizable backing:NSBackingStoreBuffered defer:NO];
  self.window.title=@"Fumo Racing · 安装与启动";[self.window center];
  NSMenu *menu=[NSMenu new];NSMenuItem *appMenu=[NSMenuItem new];[menu addItem:appMenu];NSMenu *items=[NSMenu new];
  [items addItemWithTitle:@"退出启动工具" action:@selector(terminate:) keyEquivalent:@"q"];appMenu.submenu=items;NSApp.mainMenu=menu;
  self.heading=[self label:@"导入原版游戏" size:26 color:NSColor.labelColor];self.heading.font=[NSFont systemFontOfSize:26 weight:NSFontWeightSemibold];
  NSTextField *intro=[self label:@"选择 Touhou Fumo Racing 0.2.1 的 ZIP 压缩包或已解压文件夹。\n首次导入完成后，即可开始游戏。" size:13 color:NSColor.secondaryLabelColor];
  self.status=[self label:@"正在打开安装工具…" size:14 color:NSColor.labelColor];
  self.error=[self label:@"" size:13 color:NSColor.systemRedColor];self.error.hidden=YES;self.error.maximumNumberOfLines=3;self.error.lineBreakMode=NSLineBreakByTruncatingTail;
  self.progress=[NSProgressIndicator new];self.progress.style=NSProgressIndicatorStyleBar;self.progress.indeterminate=YES;
  self.progress.minValue=0;self.progress.maxValue=1;self.progress.hidden=YES;
  self.zipButton=[self button:@"选择原版 ZIP" action:@selector(selectZip:)];self.zipButton.enabled=NO;
  self.folderButton=[self button:@"选择原版文件夹" action:@selector(selectFolder:)];self.folderButton.enabled=NO;
  self.launchButton=[self button:@"开始游戏" action:@selector(launchGame:)];self.launchButton.hidden=YES;
  NSStackView *stack=[NSStackView stackViewWithViews:@[self.heading,intro,self.status,self.progress,self.zipButton,self.folderButton,self.launchButton,self.error]];
  stack.orientation=NSUserInterfaceLayoutOrientationVertical;stack.alignment=NSLayoutAttributeLeading;stack.spacing=15;stack.translatesAutoresizingMaskIntoConstraints=NO;
  NSView *view=self.window.contentView;[view addSubview:stack];
  [NSLayoutConstraint activateConstraints:@[[stack.leadingAnchor constraintEqualToAnchor:view.leadingAnchor constant:36],[stack.trailingAnchor constraintEqualToAnchor:view.trailingAnchor constant:-36],[stack.topAnchor constraintEqualToAnchor:view.topAnchor constant:30],[stack.bottomAnchor constraintLessThanOrEqualToAnchor:view.bottomAnchor constant:-24]]];
  for(NSView *child in stack.arrangedSubviews)[child.widthAnchor constraintEqualToAnchor:stack.widthAnchor].active=YES;
  [self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
  self.engine=[NSTask new];self.engine.executableURL=[NSURL fileURLWithPath:[NSBundle.mainBundle.bundlePath stringByAppendingPathComponent:@"Contents/MacOS/engine"]];
  NSMutableArray *arguments=[NSMutableArray arrayWithObject:@"--headless"];
  for(NSString *argument in [NSProcessInfo.processInfo.arguments subarrayWithRange:NSMakeRange(1,NSProcessInfo.processInfo.arguments.count-1)])if(![argument hasPrefix:@"-psn_"])[arguments addObject:argument];
  self.engine.arguments=arguments;
  NSPipe *output=[NSPipe pipe];self.engine.standardOutput=output;
  NSError *failure=nil;
  if(![self.engine launchAndReturnError:&failure]){[self showError:@"无法打开安装工具，请重新下载并完整解压发布包。"];return;}
  __weak LauncherHost *weak=self;
  dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0),^{
    NSMutableData *line=[NSMutableData data];while(line.length<4096){NSData *byte=[output.fileHandleForReading readDataOfLength:1];if(!byte.length||((char*)byte.bytes)[0]=='\n')break;[line appendData:byte];}
    NSURLComponents *parts=[NSURLComponents componentsWithString:[[NSString alloc]initWithData:line encoding:NSUTF8StringEncoding]];
    NSString *token=nil;for(NSURLQueryItem *item in parts.queryItems)if([item.name isEqualToString:@"token"])token=item.value;
    if(![parts.scheme isEqualToString:@"http"]||![parts.host isEqualToString:@"127.0.0.1"]||token.length!=64){dispatch_async(dispatch_get_main_queue(),^{[weak showError:@"安装工具未能启动，请关闭后重试。"];});return;}
    parts.path=@"";parts.query=nil;NSURL *origin=parts.URL;
    dispatch_async(dispatch_get_main_queue(),^{LauncherHost *host=weak;if(!host)return;host.origin=origin;host.token=token;
      host.timer=[NSTimer scheduledTimerWithTimeInterval:0.7 repeats:YES block:^(NSTimer *timer){[weak poll];}];[host poll];});
  });
}
- (void)showError:(NSString *)message { self.error.stringValue=message?:@"";self.error.toolTip=message;self.error.hidden=!message.length; }
- (NSMutableURLRequest *)request:(NSString *)route {
  NSMutableURLRequest *request=[NSMutableURLRequest requestWithURL:[NSURL URLWithString:route relativeToURL:self.origin]];
  [request setValue:self.token forHTTPHeaderField:@"X-Fumo-Token"];request.timeoutInterval=15;return request;
}
- (void)poll {
  if(self.polling||!self.origin)return;self.polling=YES;
  __weak LauncherHost *weak=self;
  [[NSURLSession.sharedSession dataTaskWithRequest:[self request:@"/api/state"] completionHandler:^(NSData *data,NSURLResponse *response,NSError *error){
    NSDictionary *state=data?[NSJSONSerialization JSONObjectWithData:data options:0 error:nil]:nil;
    dispatch_async(dispatch_get_main_queue(),^{LauncherHost *host=weak;if(!host)return;host.polling=NO;
      if(![state isKindOfClass:NSDictionary.class]){[host showError:@"无法连接安装工具，请关闭后重新打开。"];return;}
      BOOL busy=[state[@"busy"] boolValue],ready=[state[@"canLaunch"] boolValue];
      host.heading.stringValue=ready?@"游戏已安装":busy?@"正在导入原版游戏":@"导入原版游戏";
      host.status.stringValue=[state[@"message"] isKindOfClass:NSString.class]?state[@"message"]:@"";
      host.zipButton.hidden=ready;host.folderButton.hidden=ready;host.launchButton.hidden=!ready;
      host.zipButton.enabled=!busy&&!host.requesting;host.folderButton.enabled=!busy&&!host.requesting;host.launchButton.enabled=!busy&&!host.requesting;
      host.progress.hidden=!busy;host.progress.indeterminate=[state[@"indeterminate"] boolValue];
      if(host.progress.indeterminate)[host.progress startAnimation:nil];else{[host.progress stopAnimation:nil];host.progress.doubleValue=[state[@"progress"] doubleValue];}
      [host showError:[state[@"error"] isKindOfClass:NSString.class]?state[@"error"]:@""];
    });
  }]resume];
}
- (void)post:(NSString *)route body:(NSDictionary *)body {
  if(self.requesting||!self.origin)return;self.requesting=YES;self.zipButton.enabled=NO;self.folderButton.enabled=NO;self.launchButton.enabled=NO;
  NSMutableURLRequest *request=[self request:route];request.HTTPMethod=@"POST";[request setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
  request.HTTPBody=[NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
  __weak LauncherHost *weak=self;
  [[NSURLSession.sharedSession dataTaskWithRequest:request completionHandler:^(NSData *data,NSURLResponse *response,NSError *error){
    NSDictionary *result=data?[NSJSONSerialization JSONObjectWithData:data options:0 error:nil]:nil;
    dispatch_async(dispatch_get_main_queue(),^{LauncherHost *host=weak;if(!host)return;host.requesting=NO;
      if(error)[host showError:@"操作未完成，请稍后重试。"];else if([(NSHTTPURLResponse*)response statusCode]!=200)[host showError:[result[@"error"] isKindOfClass:NSString.class]?result[@"error"]:@"操作失败，请重试。"];
      [host poll];
    });
  }]resume];
}
- (void)chooseDirectory:(BOOL)directory {
  NSOpenPanel *panel=[NSOpenPanel openPanel];panel.canChooseDirectories=directory;panel.canChooseFiles=!directory;panel.allowsMultipleSelection=NO;
  panel.prompt=@"导入";panel.message=directory?@"选择 Touhou Fumo Racing 0.2.1 的已解压文件夹":@"选择 Touhou Fumo Racing 0.2.1 的 ZIP 压缩包";
  if(!directory)panel.allowedContentTypes=@[UTTypeZIP];
  __weak LauncherHost *weak=self;
  [panel beginSheetModalForWindow:self.window completionHandler:^(NSModalResponse response){if(response==NSModalResponseOK)[weak post:@"/api/install" body:@{@"sourcePath":panel.URL.path}];}];
}
- (void)selectZip:(id)sender { [self chooseDirectory:NO]; }
- (void)selectFolder:(id)sender { [self chooseDirectory:YES]; }
- (void)launchGame:(id)sender { [self post:@"/api/launch" body:@{}]; }
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)application { return YES; }
- (void)applicationWillTerminate:(NSNotification *)notification { [self.timer invalidate];if(self.engine.running)[self.engine terminate]; }
@end

int main(int argc,const char **argv){@autoreleasepool{[NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];LauncherHost *host=[LauncherHost new];NSApp.delegate=host;[NSApp run];return 0;}}
