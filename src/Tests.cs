using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ShelfDock {
 static class Tests {
  static int checks;
  static Shelf CreateShelf(string root,bool silent=false) { return new Shelf(root,silent,false); }
  static void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }
  internal static void InstanceProbe(string[] args) {
   string result=args[1]; string name=args[2]; int hold=int.Parse(args[3]); bool silent=args[4]=="silent";
   try {
    using(var coordinator=new InstanceCoordinator(name,new Version(args[5]))) {
     if(!coordinator.Enter(silent)) { File.WriteAllText(result,"SECOND: existing instance reused\r\n"); return; }
     using(var icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)) using(var tray=new StableTrayIcon(new Guid(args[6]))) {
     tray.Text="ShelfDock isolated regression test"; tray.Icon=icon; tray.Visible=true;
     File.WriteAllText(result,"OWNER: instance acquired\r\n");
     var until=DateTime.UtcNow.AddMilliseconds(hold);
     while(DateTime.UtcNow<until) { Application.DoEvents(); if(coordinator.ExitEvent.WaitOne(20)) { File.AppendAllText(result,"EXIT\r\n"); break; } if(coordinator.ShowEvent.WaitOne(0)) File.AppendAllText(result,"SHOW\r\n"); }
     }
    }
   } catch(Exception ex) { File.WriteAllText(result,"ERROR: "+ex); }
  }
  static System.Diagnostics.Process Probe(string executable,string result,string name,string version,int hold,bool silent) {
   return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable,String.Join(" ",new[]{"--instance-probe",result,name,hold.ToString(),silent ? "silent" : "show",version,name.Substring(name.LastIndexOf('.')+1)}.Select(StandardUserLauncher.QuoteArgument))) { UseShellExecute=false,CreateNoWindow=true });
  }
  static void WaitForFile(string file) { var clock=System.Diagnostics.Stopwatch.StartNew(); while(!File.Exists(file) && clock.ElapsedMilliseconds<5000) System.Threading.Thread.Sleep(10); Assert(File.Exists(file),"Instance probe started"); }
  static void TestInstances(string dir) {
   string name="Local\\ShelfDock.Test."+Guid.NewGuid().ToString("N");
   string copy=Path.Combine(dir,"PortableCopy.exe"); File.Copy(Application.ExecutablePath,copy);
   string first=Path.Combine(dir,"instance-first.txt"), second=Path.Combine(dir,"instance-second.txt"), newer=Path.Combine(dir,"instance-newer.txt"), older=Path.Combine(dir,"instance-older.txt");
   using(var owner=Probe(Application.ExecutablePath,first,name,"1.23.0.0",5000,false)) {
    WaitForFile(first);
    using(var duplicate=Probe(copy,second,name,"1.23.0.0",200,false)) Assert(duplicate.WaitForExit(4000),"Duplicate launch returns promptly");
    Assert(File.ReadAllText(second).StartsWith("SECOND:"),"Different executable paths reuse one owner");
    System.Threading.Thread.Sleep(100); Assert(File.ReadAllText(first).Contains("SHOW"),"Duplicate requests Show on the existing instance");
    using(var replacement=Probe(copy,newer,name,"1.24.0.0",800,false)) {
     WaitForFile(newer); Assert(File.ReadAllText(newer).StartsWith("OWNER:") && owner.WaitForExit(1000) && File.ReadAllText(first).Contains("EXIT"),"Newer version acquires only after previous owner exits");
     using(var old=Probe(Application.ExecutablePath,older,name,"1.23.0.0",200,true)) Assert(old.WaitForExit(3000),"Older silent launch returns promptly");
     Assert(File.ReadAllText(older).StartsWith("SECOND:"),"Older version reuses newer owner");
     Assert(replacement.WaitForExit(3000),"Owner can exit cleanly");
    }
   }
   string concurrentName="Local\\ShelfDock.Test."+Guid.NewGuid().ToString("N"); var files=Enumerable.Range(0,6).Select(n=>Path.Combine(dir,"concurrent-"+n+".txt")).ToArray();
   var children=files.Select(f=>Probe(copy,f,concurrentName,"1.23.0.0",1500,true)).ToArray();
   try { foreach(var child in children) Assert(child.WaitForExit(5000),"Concurrent launch completes"); Assert(files.Count(f=>File.ReadAllText(f).StartsWith("OWNER:"))==1,"Six simultaneous launches produce exactly one owner"); } finally { foreach(var child in children) child.Dispose(); }
  }
  static void TestContext(string dir) {
   foreach(int menuIndex in new[]{1,2}) using(var instance=new InstanceCoordinator("Local\\ShelfDock.ContextTest."+Guid.NewGuid().ToString("N"),new Version(1,23,0,0))) {
    Assert(instance.Enter(true),"Isolated context acquires ownership");
    var shelf=CreateShelf(Path.Combine(dir,"context-"+menuIndex),true); int stage=0; string failure=null;
    using(var context=new ShelfContext(shelf,instance,true)) using(var timer=new Timer { Interval=120 }) {
     var clock=System.Diagnostics.Stopwatch.StartNew();
     timer.Tick+=(s,e)=> {
      if(clock.ElapsedMilliseconds>5000) { failure="Context did not quit after IPC Exit"; shelf.Quit(); Application.ExitThread(); return; }
      if(stage==0) { Assert(!shelf.Visible,"Silent context starts hidden"); instance.ShowEvent.Set(); stage=1; }
      else if(stage==1) { Assert(shelf.Visible,"Hidden context processes IPC Show"); shelf.Hide(); stage=2; shelf.TrayMenu.Items[menuIndex].PerformClick(); }
      else if(stage==2 && shelf.OwnedForms.Any(f=>f.Visible)) { stage=3; instance.ExitEvent.Set(); }
     };
     timer.Start(); Application.Run(context);
     Assert(failure==null && stage==3 && shelf.IsDisposed,"IPC Exit closes modal dialog and ends independent message loop: "+menuIndex);
    }
   }
  }
  internal static void Run(string result) {
   string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result)), "test-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
   try {
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    TestInstances(dir);
    TestContext(dir);
    string file = Path.Combine(dir,"中文 file.txt"); File.WriteAllText(file,"original content"); string folder = Path.Combine(dir,"folder"); Directory.CreateDirectory(folder);
    string imagePath;
    Assert(StandardUserLauncher.IsElevated == StandardUserLauncher.IsShellElevated,"Regression tests run at the desktop shell permission level");
    Assert(StandardUserLauncher.QuoteArgument("C:\\folder with space\\") == "\"C:\\folder with space\\\\\"","Launcher quotes trailing path separators");
    Assert(StandardUserLauncher.QuoteArgument("a\"b") == "\"a\\\"b\"","Launcher escapes embedded quotes");
    string probe=Path.Combine(dir,"launch result.txt");
    int launched=StandardUserLauncher.Start(Application.ExecutablePath,new[]{"--launch-probe",probe,StandardUserLauncher.IsShellElevated ? "elevated" : "standard","中文 path with spaces","a\"b","C:\\tail\\"});
    try { using(var child=System.Diagnostics.Process.GetProcessById(launched)) Assert(child.WaitForExit(10000),"Standard-user launch completes"); } catch(ArgumentException) {}
    Assert(File.ReadAllText(probe)=="PASS: matching desktop permission level\r\n中文 path with spaces\r\na\"b\r\nC:\\tail\\","Launched process matches desktop permission level and receives original Unicode arguments");
    using (var app = CreateShelf(Path.Combine(dir,"store"))) {
     // A hidden shelf still owns a real message-loop handle, so every tray command must execute.
     app.Hide(); app.TrayMenu.Items[0].PerformClick(); Pump(160); Assert(app.Visible,"Hidden shelf opens from tray Show command");
     app.Hide();
     bool settingsSeen=false,helpSeen=false;
     using(var closeSettings=new Timer { Interval=80 }) { closeSettings.Tick+=(s,e)=> { foreach(Form f in Application.OpenForms) if(f.Text=="拾放设置") { settingsSeen=true; closeSettings.Stop(); f.Close(); break; } }; closeSettings.Start(); app.TrayMenu.Items[1].PerformClick(); Pump(220); Assert(settingsSeen && app.Visible,"Hidden shelf opens settings from tray"); }
     app.Hide();
     using(var closeHelp=new Timer { Interval=80 }) { closeHelp.Tick+=(s,e)=> { foreach(Form f in Application.OpenForms) if(f.Text=="拾放 · 使用帮助") { helpSeen=true; closeHelp.Stop(); f.Close(); break; } }; closeHelp.Start(); app.TrayMenu.Items[2].PerformClick(); Pump(220); Assert(helpSeen && app.Visible,"Hidden shelf opens help from tray"); }
     app.Hide();
     app.Show(); Application.DoEvents(); using (var preview = new Bitmap(app.Width,app.Height)) { app.DrawToBitmap(preview,app.ClientRectangle); preview.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result)),"preview-empty.png")); } app.Hide(); app.CompleteAnimationForTests();
     Assert(app.Items.Length == 0,"New shelf should be empty");
     var data = new DataObject(); data.SetData(DataFormats.FileDrop,new[]{file,folder,Path.Combine(dir,"missing")}); app.AddData(data);
     Assert(app.Items.Length == 1 && app.Items[0].Kind == "group" && app.Items[0].Paths.SequenceEqual(new[]{file,folder}),"Multiple files form a group; nonexistent paths ignored");
     var output = app.MakeSelectionData(app.Items); Assert(((string[])output.GetData(DataFormats.FileDrop)).SequenceEqual(new[]{file,folder}),"Multi-file outbound shell format");
     data = new DataObject(); data.SetData(DataFormats.FileDrop,new[]{folder}); app.AddData(data);
     Assert(app.Items.Last().Kind == "file", "Single file remains an individual item");
     Assert(((string[])app.MakeSelectionData(app.Items).GetData(DataFormats.FileDrop)).SequenceEqual(new[]{file,folder,folder}), "Groups and individual files drag together");
     app.Settings.EdgeEnabled = false; app.Settings.EdgeSide = "left"; app.Settings.EdgePercent = 25; app.SavePreferences();
     Assert(Shelf.AtEdge(new Point(-100,20),new Rectangle(-100,0,100,100),"left",10) && !Shelf.AtEdge(new Point(-100,20),new Rectangle(-100,0,100,100),"right",10) && Shelf.AtEdge(new Point(-1,20),new Rectangle(-100,0,100,100),"right",10),"Both screen edges including negative monitor coordinates");
     Assert(Shelf.AtEdge(new Point(900,20),new Rectangle(0,0,1000,100),"right",10) && !Shelf.AtEdge(new Point(899,20),new Rectangle(0,0,1000,100),"right",10),"Right boundary is ten percent of screen width");
     Assert(Shelf.AtEdge(new Point(1800,20),new Rectangle(0,0,2000,100),"right",10) && !Shelf.AtEdge(new Point(1799,20),new Rectangle(0,0,2000,100),"right",10),"Range scales with screen resolution");
     Assert(Shelf.AtEdge(new Point(-1901,20),new Rectangle(-2000,0,1000,100),"left",10) && !Shelf.AtEdge(new Point(-1900,20),new Rectangle(-2000,0,1000,100),"left",10),"Left boundary uses same proportional range with negative coordinates");
     data = new DataObject(); data.SetData(DataFormats.UnicodeText,"第一行\nsecond line 🌿"); app.AddData(data);
     data = new DataObject(); data.SetData(DataFormats.UnicodeText,"https://example.com/?a=1&b=2"); app.AddData(data);
     var textItems = app.Items.Where(x => x.Kind == "text").ToArray();
     Assert((string)app.MakeSelectionData(textItems).GetData(DataFormats.UnicodeText) == textItems[0].Value + Environment.NewLine + textItems[1].Value,"Multiline Unicode and joined text round trip");
     var single = app.MakeSelectionData(new[]{textItems[0]}); var exported = ((string[])single.GetData(DataFormats.FileDrop))[0]; Assert(File.ReadAllText(exported) == textItems[0].Value,"Text also exports as a real UTF-8 file");
     single = app.MakeSelectionData(new[]{textItems[1]}); exported = ((string[])single.GetData(DataFormats.FileDrop))[0]; Assert(exported.EndsWith(".url") && File.ReadAllText(exported).Contains("https://example.com/?a=1&b=2"),"Links export as Internet shortcuts");
     bool rejected = false; try { app.MakeSelectionData(app.Items); } catch (InvalidOperationException) { rejected = true; } Assert(rejected,"Mixed selections must fail clearly");
     data = new DataObject(); data.SetData(DataFormats.Bitmap,new Bitmap(12,8)); app.AddData(data); imagePath = app.Items.Last().Value;
     Assert(File.Exists(imagePath),"Bitmap persisted as PNG"); using (var img = Image.FromFile(imagePath)) Assert(img.Width == 12 && img.Height == 8,"PNG dimensions preserved");
     Assert(((string[])app.MakeSelectionData(new[]{app.Items.Last()}).GetData(DataFormats.FileDrop))[0] == imagePath,"Image can be dragged to file destinations");
     Assert(File.ReadAllText(file) == "original content","Source file unchanged");
     app.Show(); Application.DoEvents(); using (var preview = new Bitmap(app.Width,app.Height)) { app.DrawToBitmap(preview,new Rectangle(0,0,app.Width,app.Height)); preview.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result)),"preview-items.png")); } app.Hide(); app.CompleteAnimationForTests();
     File.Delete(file); rejected = false; try { app.MakeSelectionData(new[]{app.Items[0]}); } catch (IOException) { rejected = true; } Assert(rejected,"Missing files rejected before outbound drag");
     app.AddData(null); Assert(app.Items.Length == 5,"Null payload safe");
    }
    using (var restored = CreateShelf(Path.Combine(dir,"store"))) { Assert(restored.Items.Length == 5,"All entries restored from atomic JSON store"); Assert(restored.Items[0].Paths.SequenceEqual(new[]{file,folder}),"Group members restored"); Assert(!restored.Settings.EdgeEnabled && restored.Settings.EdgeSide == "left" && restored.Settings.EdgePercent == 25,"Settings including percentage restored"); Assert(restored.Left == Screen.PrimaryScreen.WorkingArea.Left + 16,"Saved left preference positions startup window on the left"); Assert(restored.Items[2].Value == "第一行\nsecond line 🌿","Unicode persisted exactly"); Assert(restored.Items.Last().Value == imagePath,"Image reference restored"); }
    using (var app = CreateShelf(Path.Combine(dir,"startup-right"))) { Assert(app.Left == Screen.PrimaryScreen.WorkingArea.Right - app.Width - 16,"Default right preference positions startup window on the right"); app.Settings.EdgeSide = "right"; app.SavePreferences(); }
    using (var app = CreateShelf(Path.Combine(dir,"startup-right"))) Assert(app.Left == Screen.PrimaryScreen.WorkingArea.Right - app.Width - 16,"Saved right preference positions startup window on the right");
    foreach (string side in new[]{"left","right"}) using (var app = CreateShelf(Path.Combine(dir,"click-edge-"+side))) {
     app.StopEdgePollingForTests(); app.Settings.EdgeSide = side;
     var screen = Screen.PrimaryScreen.Bounds; var point = new Point(side == "left" ? screen.Left : screen.Right-1,screen.Top+screen.Height/2);
     app.UpdateEdgeVisibility(true,point); app.CompleteAnimationForTests(); Assert(!app.Visible,"Click at edge cannot activate: "+side);
     for (int n=0;n<30;n++) app.UpdateEdgeVisibility(true,point);
     app.CompleteAnimationForTests(); Assert(!app.Visible,"Long press at edge cannot activate: "+side);
     app.UpdateEdgeVisibility(true,new Point(point.X,point.Y+40)); app.CompleteAnimationForTests(); Assert(!app.Visible,"Held movement along edge cannot activate: "+side);
     app.UpdateEdgeVisibility(true,new Point(screen.Left+screen.Width/2,point.Y)); app.UpdateEdgeVisibility(true,point); app.CompleteAnimationForTests(); Assert(!app.Visible,"Held movement into edge cannot activate: "+side);
     app.UpdateEdgeVisibility(false,point); app.CompleteAnimationForTests(); Assert(!app.Visible,"Button release cannot activate: "+side);
     var payload = new DataObject(DataFormats.FileDrop,new[]{folder});
     Assert(app.HandleEdgeDrag(payload,DragDropEffects.Copy,point),"Real file payload activates: "+side); app.CompleteAnimationForTests(); Assert(app.Visible,"Real content reveals shelf: "+side); app.Hide(); app.CompleteAnimationForTests();
     Assert(!app.HandleEdgeDrag(null,DragDropEffects.Copy,point) && !app.Visible,"Null payload cannot activate: "+side);
     Assert(!app.HandleEdgeDrag(new DataObject("unsupported",new object()),DragDropEffects.Copy,point) && !app.Visible,"Unsupported payload cannot activate: "+side);
     Assert(!app.HandleEdgeDrag(payload,DragDropEffects.None,point) && !app.Visible,"Rejected effects cannot activate: "+side);
     Assert(app.HandleEdgeDrag(new DataObject(DataFormats.UnicodeText,"真实文字拖放"),DragDropEffects.Copy,point),"Real text activates: "+side); app.Hide(); app.CompleteAnimationForTests();
    }
    using (var app = CreateShelf(Path.Combine(dir,"edge-store"))) {
     app.StopEdgePollingForTests();
     var screen = Screen.PrimaryScreen.Bounds;
     var right = new Point(screen.Right-1,screen.Top+screen.Height/2);
     var away = new Point(screen.Left+screen.Width/2,screen.Top+screen.Height/2);
     Assert(app.Settings.EdgePercent == 10,"Default edge range is ten percent");
     var boundary = new Point(screen.Right-(int)Math.Ceiling(screen.Width*0.1),right.Y);
     var outside = new Point(boundary.X-1,right.Y);
     app.UpdateEdgeVisibility(true,outside,true); app.CompleteAnimationForTests(); Assert(!app.Visible,"Drag outside configured range does not activate");
     app.UpdateEdgeVisibility(true,boundary,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Entering ten percent boundary activates before reaching edge");
     app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests(); app.UpdateEdgeVisibility(true,boundary,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Moving away from edge within same range stays visible");
     var inside = new Point(app.Left+20,app.Top+20); app.UpdateEdgeVisibility(true,inside,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Dragging inside the whole window stays visible beyond the activation range");
     app.UpdateEdgeVisibility(true,away,true); app.CompleteAnimationForTests(); Assert(!app.Visible,"Leaving the whole window while dragging hides");
     app.UpdateEdgeVisibility(true,away,true);
     app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Edge drag shows preview");
     app.UpdateEdgeVisibility(false,right,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Releasing at edge keeps preview visible"); app.UpdateEdgeVisibility(false,away,true); Pump(250); Assert(app.Visible,"Leaving edge after release keeps the window open"); app.Hide(); app.CompleteAnimationForTests(); app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests();
     app.UpdateEdgeVisibility(true,away,true); app.CompleteAnimationForTests(); Assert(!app.Visible && !app.IsDisposed,"Leaving edge while holding mouse hides without exiting");
     app.UpdateEdgeVisibility(false,away,true); app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests(); app.UpdateEdgeVisibility(false,away,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Release outside the window does not start a new hide"); app.Hide(); app.CompleteAnimationForTests();
     app.ShowShelf(); app.UpdateEdgeVisibility(false,away,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Manual opening stays visible away from edge"); app.Hide(); app.CompleteAnimationForTests();
     app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests();
     var data = new DataObject(); data.SetData(DataFormats.UnicodeText,"已投放"); app.AcceptDrop(data);
     app.UpdateEdgeVisibility(false,away,true); app.CompleteAnimationForTests(); Assert(app.Visible && app.Items.Length == 1,"Accepted drop retains content and window"); app.Hide(); app.CompleteAnimationForTests();
     app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests(); app.ShowShelf(); app.UpdateEdgeVisibility(true,away,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Manual show cancels edge preview"); app.Hide(); app.CompleteAnimationForTests();
     app.Settings.EdgeSide = "left"; var left = new Point(screen.Left,screen.Top+screen.Height/2);
     app.UpdateEdgeVisibility(true,left,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Left-edge preview also shows"); app.UpdateEdgeVisibility(true,away,true); app.CompleteAnimationForTests(); Assert(!app.Visible && app.Items.Length == 1,"Leaving left edge hides and preserves content");
     app.Settings.EdgeEnabled = false; app.UpdateEdgeVisibility(true,left,true); app.CompleteAnimationForTests(); Assert(!app.Visible,"Disabled edge activation stays hidden");
    }
    foreach (string side in new[]{"left","right"}) using (var app = CreateShelf(Path.Combine(dir,"window-departure-"+side))) {
     app.StopEdgePollingForTests(); app.Settings.EdgeSide = side; app.Settings.EdgePercent = 50;
     var screen = Screen.PrimaryScreen.Bounds; var trigger = new Point(side == "left" ? screen.Left : screen.Right-1,screen.Top+screen.Height/2); var away = new Point(side == "left" ? screen.Right-1 : screen.Left,trigger.Y);
     app.UpdateEdgeVisibility(true,trigger,true); app.CompleteAnimationForTests();
     var inside = new Point(app.Left+20,app.Top+20); var outsideWindow = new Point(inside.X,app.Top-1);
     app.UpdateEdgeVisibility(true,inside,true); Pump(60); Assert(app.Visible && app.Opacity == 1,"Title and header stay open while dragging: "+side);
     Assert(Shelf.AtEdge(outsideWindow,screen,side,50),"Window exit point remains inside activation threshold: "+side);
     app.UpdateEdgeVisibility(true,outsideWindow,true); Pump(60); Assert(app.Visible && app.Opacity < 1 && app.Opacity > 0,"Window exit immediately fades: "+side);
     app.UpdateEdgeVisibility(true,inside,true); Pump(220); Assert(app.Visible && app.Opacity == 1,"Reentering the window reverses fade: "+side);
     app.UpdateEdgeVisibility(true,outsideWindow,true); Pump(230); Assert(!app.Visible && !app.IsDisposed,"Window exit hides without quitting: "+side);
     app.UpdateEdgeVisibility(true,outsideWindow,true); Assert(!app.Visible,"Still inside edge threshold does not repeatedly reopen: "+side);
     app.UpdateEdgeVisibility(true,away,true); app.UpdateEdgeVisibility(true,trigger,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Next approach to edge reactivates normally: "+side);
     app.Hide(); app.CompleteAnimationForTests();
     var wideTrigger = new Point(side == "left" ? screen.Left+screen.Width/2-1 : screen.Right-screen.Width/2,trigger.Y);
     app.UpdateEdgeVisibility(true,away,true); app.UpdateEdgeVisibility(true,wideTrigger,true); app.CompleteAnimationForTests();
     if (!app.Bounds.Contains(wideTrigger)) {
      app.UpdateEdgeVisibility(true,wideTrigger,true); Assert(app.Visible,"Wide threshold does not dismiss a stationary drag before reaching window: "+side);
      var closer = new Point(wideTrigger.X+(side == "left" ? -10 : 10),wideTrigger.Y); app.UpdateEdgeVisibility(true,closer,true); app.CompleteAnimationForTests(); Assert(app.Visible,"Approaching window from wide threshold stays open: "+side);
      app.UpdateEdgeVisibility(true,wideTrigger,true); Pump(230); Assert(!app.Visible,"Dragging back away before reaching window also fades: "+side);
     }
    }
    var legacyDir = Path.Combine(dir,"legacy-settings"); Directory.CreateDirectory(legacyDir); File.WriteAllText(Path.Combine(legacyDir,"settings.json"),"{\"EdgeEnabled\":false,\"EdgeSide\":\"left\"}");
    using (var app = CreateShelf(legacyDir)) Assert(!app.Settings.EdgeEnabled && app.Settings.EdgeSide == "left" && app.Settings.EdgePercent == 10 && app.Settings.EmptyCloseDelayHalfSeconds == 1 && app.Settings.CloseToTray,"Legacy settings retain preferences and default window close to the tray");
    for (int oldDelay = 0; oldDelay <= 4; oldDelay++) {
     var settingsDir = Path.Combine(dir,"removed-edge-delay-"+oldDelay); Directory.CreateDirectory(settingsDir); var settingsPath = Path.Combine(settingsDir,"settings.json");
     File.WriteAllText(settingsPath,"{\"EdgeEnabled\":true,\"EdgeSide\":\"right\",\"EdgePercent\":10,\"EdgeCloseDelayHalfSeconds\":"+oldDelay+",\"EmptyCloseDelayHalfSeconds\":2}");
     using (var app = CreateShelf(settingsDir)) {
      app.StopEdgePollingForTests(); Assert(app.Settings.EmptyCloseDelayHalfSeconds == 2,"Old settings preserve empty-shelf delay: "+oldDelay); app.SavePreferences(); Assert(!File.ReadAllText(settingsPath).Contains("EdgeCloseDelayHalfSeconds"),"Saving drops removed edge delay: "+oldDelay);
      var screen = Screen.PrimaryScreen.Bounds; var right = new Point(screen.Right-1,screen.Top+screen.Height/2); var away = new Point(screen.Left+screen.Width/2,right.Y);
      app.UpdateEdgeVisibility(true,right,true); app.CompleteAnimationForTests(); app.UpdateEdgeVisibility(false,right,true); app.UpdateEdgeVisibility(false,away,true); Pump(oldDelay == 4 ? 2250 : 220); Assert(app.Visible && app.Opacity == 1,"Old edge delay no longer hides after release: "+oldDelay);
      app.Hide(); app.CompleteAnimationForTests();
     }
    }
    for (int step = 0; step <= 4; step++) {
     var settingsDir = Path.Combine(dir,"independent-delay-" + step);
     using (var app = CreateShelf(settingsDir)) { app.Settings.EmptyCloseDelayHalfSeconds = step; app.SavePreferences(); }
     using (var app = CreateShelf(settingsDir)) Assert(app.Settings.EmptyCloseDelayHalfSeconds == step,"Each empty-shelf half-second choice persists: " + step);
    }
    var invalidDir = Path.Combine(dir,"invalid-delay"); Directory.CreateDirectory(invalidDir); File.WriteAllText(Path.Combine(invalidDir,"settings.json"),"{\"EmptyCloseDelayHalfSeconds\":5,\"EdgeCloseDelayHalfSeconds\":-1}");
    using (var app = CreateShelf(invalidDir)) Assert(app.Settings.EmptyCloseDelayHalfSeconds == 1,"Invalid delays fall back to half a second");
    using (var app = CreateShelf(Path.Combine(dir,"animation-store"),true)) {
     app.StopEdgePollingForTests(); app.Show(); Application.DoEvents();
     Assert(!app.Visible && app.IsHandleCreated && !app.ShowInTaskbar,"Silent initial show creates message handle without window or taskbar entry");
     Assert(app.Settings.CloseToTray,"Window close defaults to minimizing to tray");
     var screen = Screen.PrimaryScreen.Bounds; var right = new Point(screen.Right-1,screen.Top+screen.Height/2); var away = new Point(screen.Left+screen.Width/2,right.Y);
     app.UpdateEdgeVisibility(true,right,true); Assert(app.Visible && app.Opacity < 0.05,"Edge preview begins transparent");
     Pump(85); Assert(app.Opacity > 0 && app.Opacity < 1,"Fade-in has intermediate opacity");
     app.UpdateEdgeVisibility(true,away,true); double partial = app.Opacity; Pump(60); Assert(app.Visible && app.Opacity < partial,"Fade-out begins from current opacity");
     app.UpdateEdgeVisibility(true,right,true); partial = app.Opacity; Pump(70); Assert(app.Visible && app.Opacity > partial,"Returning to edge reverses fade smoothly");
     Pump(200); Assert(app.Visible && app.Opacity == 1,"Fade-in completes fully opaque");
     app.UpdateEdgeVisibility(true,away,true); Pump(250); Assert(!app.Visible && !app.IsDisposed,"Fade-out really hides without exiting");
     app.ShowShelf(); Pump(250); Assert(app.Visible && app.Opacity == 1 && app.ShowInTaskbar,"Silent app can be opened normally");
     app.Close(); Assert(app.Visible && !app.IsDisposed,"Close starts animation rather than disposing"); Pump(250); Assert(!app.Visible,"Close animation ends hidden");
     app.UpdateEdgeVisibility(true,right,true); Pump(60); app.UpdateEdgeVisibility(true,away,true); var drop = new DataObject(); drop.SetData(DataFormats.UnicodeText,"during fade"); app.AcceptDrop(drop); Pump(250); Assert(app.Visible && app.Opacity == 1 && app.Items.Length == 1,"Accepted drop cancels pending fade-out");
     var first = app.Items[0]; app.CompleteOutboundDrag(DragDropEffects.None,new[]{first}); Assert(app.Visible && app.Items.Length == 1,"Cancelled or rejected drag preserves contents and window");
     drop = new DataObject(); drop.SetData(DataFormats.UnicodeText,"another item"); app.AddData(drop);
     app.CompleteOutboundDrag(DragDropEffects.Copy,new[]{first}); Pump(220); Assert(app.Visible && app.Items.Length == 1 && app.Items[0].Value == "another item","Successful drop removes only dragged entry and keeps window open");
     using (var restored = CreateShelf(Path.Combine(dir,"animation-store"))) Assert(restored.Items.Length == 1 && restored.Items[0].Value == "another item","Remaining entries persist after drag");
     app.CompleteOutboundDrag(DragDropEffects.Copy,app.Items); Assert(app.Visible && app.Items.Length == 0,"Last outbound entry starts fade");
     Pump(70); Assert(app.Visible && app.Opacity < 1 && app.Opacity > 0,"Successful outbound drop fades gradually");
     Pump(200); Assert(!app.Visible && !app.IsDisposed,"Outbound fade hides without exiting");
     var groupData = new DataObject(); groupData.SetData(DataFormats.FileDrop,new[]{folder, imagePath}); app.AcceptDrop(groupData); Pump(220);
     Assert(app.Items.Length == 1 && app.Items[0].Kind == "group","Group remains one entry");
     app.CompleteOutboundDrag(DragDropEffects.Copy,app.Items); Pump(220); Assert(!app.Visible && app.Items.Length == 0 && Directory.Exists(folder) && File.Exists(imagePath),"Group drag removes group and preserves originals");
    }
    using (var app = CreateShelf(Path.Combine(dir,"direct-exit-store"))) { app.StopEdgePollingForTests(); app.Settings.CloseToTray = false; app.SavePreferences(); app.ShowShelf(); Pump(220); app.Close(); Assert(app.IsDisposed && !app.Visible,"Direct close option exits the application"); }
    using (var app = CreateShelf(Path.Combine(dir,"direct-exit-store"))) Assert(!app.Settings.CloseToTray,"Direct close preference persists across restarts");
    using (var restored = CreateShelf(Path.Combine(dir,"animation-store"))) Assert(restored.Items.Length == 0,"Outbound clearing persists");
    using (var app = CreateShelf(Path.Combine(dir,"remove-store"))) {
     app.StopEdgePollingForTests(); app.ShowShelf(); Pump(220); Point pointer = new Point(app.Left+30,app.Top+30); app.PointerPosition = () => pointer; var data = new DataObject(); data.SetData(DataFormats.FileDrop,new[]{folder}); app.AddData(data); data = new DataObject(); data.SetData(DataFormats.FileDrop,new[]{folder,imagePath}); app.AddData(data);
     FindRemoveButton(app).PerformClick(); Pump(750); Assert(app.Visible && app.Items.Length == 1 && app.Items[0].Kind == "group","X removes one entry and keeps remaining group visible");
     FindRemoveButton(app).PerformClick(); Pump(220); Assert(app.Visible && app.Opacity == 1 && app.Items.Length == 0,"Last X waits for its configured delay while pointer remains inside");
     Pump(180); Assert(app.Visible && app.Opacity == 1,"Last X stays fully visible until the half-second delay expires");
     Pump(160); Assert(app.Visible && app.Opacity < 1 && app.Opacity > 0,"Last X fades after the delay without waiting for pointer exit"); Pump(220); Assert(!app.Visible && !app.IsDisposed,"Last X hides without exiting");
     pointer = new Point(app.Left+30,app.Top+30); app.AcceptDrop(data); Pump(220); FindRemoveButton(app).PerformClick(); pointer = new Point(app.Right+10,app.Bottom+10); Pump(150); app.AddData(data); Pump(650); Assert(app.Visible && app.Items.Length == 1 && app.Opacity == 1,"New content cancels close delay");
     app.Settings.EmptyCloseDelayHalfSeconds = 0; pointer = new Point(app.Left+30,app.Top+30); FindRemoveButton(app).PerformClick(); Pump(70); Assert(app.Visible && app.Opacity < 1,"Immediate option starts fading while pointer remains in window"); Pump(220); Assert(!app.Visible,"Immediate option ends hidden");
     app.Settings.EmptyCloseDelayHalfSeconds = 4; pointer = new Point(app.Left+30,app.Top+30); app.AcceptDrop(data); Pump(220); FindRemoveButton(app).PerformClick(); Pump(1700); Assert(app.Visible && app.Opacity == 1,"Maximum delay counts down while pointer remains inside"); Pump(360); Assert(app.Visible && app.Opacity < 1,"Maximum delay starts fade without pointer exit"); Pump(220); Assert(!app.Visible && Directory.Exists(folder) && File.Exists(imagePath),"Maximum delay hides and preserves originals");
     for (int step = 2; step <= 3; step++) {
      app.Settings.EmptyCloseDelayHalfSeconds = step; app.AcceptDrop(data); Pump(220); FindRemoveButton(app).PerformClick(); Pump(step*500-250); Assert(app.Visible && app.Opacity == 1,"Intermediate delay stays visible before expiry: "+step);
      pointer = new Point(app.Right+10,app.Bottom+10); Pump(100); pointer = new Point(app.Left+30,app.Top+30); Pump(400); Assert(!app.Visible && !app.IsDisposed,"Pointer exit and return do not reset removal countdown: "+step);
     }
     app.Settings.EmptyCloseDelayHalfSeconds = 0; app.AcceptDrop(data); Pump(220); FindRemoveButton(app).PerformClick(); Pump(60); app.AddData(data); Pump(250); Assert(app.Visible && app.Items.Length == 1 && app.Opacity == 1,"New content during removal fade restores the window");
     FindRemoveButton(app).PerformClick(); Pump(250);
    }
    using (var app = CreateShelf(Path.Combine(dir,"remove-store"))) Assert(app.Items.Length == 0,"Removal persists");
    string startupPath = Path.Combine(dir,"启动项 with spaces.lnk");
    Assert(!StartupRegistration.IsEnabled(startupPath),"No startup registration by default");
    StartupRegistration.Set(true,Application.ExecutablePath,startupPath); Assert(StartupRegistration.IsEnabled(startupPath),"Startup shortcut contains valid target and silent argument");
    StartupRegistration.Set(true,Application.ExecutablePath,startupPath); Assert(StartupRegistration.IsEnabled(startupPath),"Startup registration can be replaced atomically");
    bool invalidStartup = false; try { StartupRegistration.Set(true,Path.Combine(dir,"missing.exe"),startupPath); } catch (FileNotFoundException) { invalidStartup = true; }
    Assert(invalidStartup && StartupRegistration.IsEnabled(startupPath),"Failed startup update preserves previous registration");
    StartupRegistration.Set(false,Application.ExecutablePath,startupPath); Assert(!File.Exists(startupPath),"Disabling startup deletes shortcut");
    StartupRegistration.Set(false,Application.ExecutablePath,startupPath); Assert(!StartupRegistration.IsEnabled(startupPath),"Disabling startup is idempotent");
    Assert(Directory.Exists(folder),"Source folder unchanged");
    File.WriteAllText(result, "PASS: " + checks + " checks\r\nFile, folder, Unicode text, URL, bitmap, multi-selection, persistence, missing-file handling, and edge-preview lifecycle.\r\nTest data: " + dir);
   } catch (Exception e) { File.WriteAllText(result,"FAIL: " + e); Environment.ExitCode = 1; }
  }
  static Button FindRemoveButton(Control root) { foreach (Control child in root.Controls) { var button = child as Button; if (button != null && button.AccessibleName == "移除") return button; var found = FindRemoveButton(child); if (found != null) return found; } return null; }
  static void Pump(int milliseconds) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
 }
}

