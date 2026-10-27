using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LocalResolution {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct DevMode {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string device;
        public ushort spec, driver, size, extra;
        public uint fields;
        public int x,y;
        public uint orientation, fixedOutput;
        public short color, duplex, yResolution, ttOption, collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string form;
        public ushort logPixels;
        public uint bpp,width,height,flags,hz,icmMethod,icmIntent,media,dither,reserved1,reserved2,panningWidth,panningHeight;
        public override string ToString() { return width+" × "+height+"  /  "+hz+" Hz  /  "+bpp+" 位"; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Device {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string description;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string key;
        public override string ToString() { return name+"  "+description+((flags&4)!=0?"（主显示器）":""); }
    }
    static class Display {
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string device,uint index,ref Device result,uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string device,int index,ref DevMode result);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device,ref DevMode mode,IntPtr window,uint flags,IntPtr param);
        public static List<Device> Devices() {
            var list=new List<Device>();
            for(uint i=0;;i++) { var d=new Device {cb=Marshal.SizeOf(typeof(Device))}; if(!EnumDisplayDevices(null,i,ref d,0)) break; if((d.flags&1)!=0 && (d.flags&8)==0) list.Add(d); }
            return list.OrderByDescending(d=>(d.flags&4)!=0).ToList();
        }
        public static DevMode Read(string device,int index) {
            var m=new DevMode {size=(ushort)Marshal.SizeOf(typeof(DevMode))};
            if(!EnumDisplaySettings(device,index,ref m)) throw new InvalidOperationException("无法读取显示模式，显示器可能已断开。"); return m;
        }
        public static DevMode Current(string device) { return Read(device,-1); }
        public static List<DevMode> Modes(string device) {
            var list=new List<DevMode>();
            for(int i=0;;i++) { var m=new DevMode {size=(ushort)Marshal.SizeOf(typeof(DevMode))}; if(!EnumDisplaySettings(device,i,ref m)) break; if(m.bpp>=32 && (m.flags&2)==0) list.Add(m); }
            return list;
        }
        public static DevMode Select(string device,Preset p) {
            p.Validate(); var c=Current(device);
            var candidates=Modes(device).Where(m=>m.width==p.Width && m.height==p.Height && m.bpp==c.bpp && m.orientation==c.orientation && (p.Hz==0 || m.hz==p.Hz)).ToList();
            if(candidates.Count==0) throw new InvalidOperationException("此显示器/驱动未提供 "+p.Label+"。请从“可用模式”选择，或先在显卡控制面板创建并验证自定义模式。本工具不会强制创建模式。");
            var m1=candidates.OrderByDescending(m=>m.hz==c.hz).ThenByDescending(m=>m.hz).First();
            // Keep the current topology; request only enumerated display parameters.
            m1.fields=0x00040000|0x00080000|0x00100000|0x00200000|0x00400000;
            m1.extra=0; return m1;
        }
        public static int Set(string device,DevMode m,bool test) { m.extra=0; return ChangeDisplaySettingsEx(device,ref m,IntPtr.Zero,test?2u:0u,IntPtr.Zero); }
        public static bool Same(DevMode a,DevMode b) { return a.width==b.width && a.height==b.height && a.hz==b.hz && a.bpp==b.bpp && a.orientation==b.orientation; }
        public static string Error(int n) {
            switch(n) {case 0:return "成功"; case 1:return "需要重启才能应用，未作为成功处理"; case -1:return "显示驱动拒绝操作"; case -2:return "不支持此显示模式"; case -3:return "无法保存显示设置"; case -4:return "无效的显示标志"; case -5:return "无效的显示参数"; case -6:return "双显示模式不兼容"; default:return "系统返回 "+n;}
        }
        public static void Restore(string device,DevMode original) {
            int r=Set(device,original,false);
            if(r!=0 || !Same(Current(device),original)) throw new InvalidOperationException("恢复失败："+Error(r)+"。请打开 Windows 显示设置恢复。");
        }
    }
    public class Preset {
        public string Id=Guid.NewGuid().ToString("N");
        public string Name;
        public int Width,Height,Hz;
        public string Label {get {return Name+"  "+Width+" × "+Height+(Hz==0?" · 自动 Hz":" · "+Hz+" Hz");}}
        public void Validate() {
            if(string.IsNullOrWhiteSpace(Name)||Name.Length>50||Name.Any(char.IsControl)||Width<320||Width>16384||Height<200||Height>16384||Hz<0||Hz>1000) throw new InvalidOperationException("名称限 1–50 字；宽 320–16384，高 200–16384，刷新率 0–1000（0 为自动）。");
            Guid id; if(!Guid.TryParseExact(Id,"N",out id)) throw new InvalidOperationException("预设编号无效。");
        }
    }
    public class Config {
        public List<Preset> Presets=new List<Preset>();
        public string DefaultId,Device;
        public bool CloseToTray=true;
        public static Config Defaults() {
            var c=new Config();
            c.Presets.Add(new Preset {Name="无畏契约",Width=1280,Height=960});
            c.Presets.Add(new Preset {Name="三角洲行动",Width=1720,Height=1080});
            c.Presets.Add(new Preset {Name="日常桌面",Width=1920,Height=1080});
            c.DefaultId=c.Presets[2].Id; return c;
        }
        public void Validate() {
            if(Presets==null||Presets.Count==0||Presets.Count>100) throw new InvalidOperationException("预设数量应为 1–100。");
            foreach(var p in Presets) p.Validate();
            if(Presets.Select(p=>p.Id).Distinct().Count()!=Presets.Count||!Presets.Any(p=>p.Id==DefaultId)) throw new InvalidOperationException("默认预设或预设编号无效。");
        }
    }
    public class Recovery {public string Device; public DevMode Original; public long Deadline; public int Parent;}
    static class Store {
        public static readonly string Dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalResolutionSwitcher");
        public static readonly string FilePath=Path.Combine(Dir,"config.json");
        public static readonly string Journal=Path.Combine(Dir,"pending.json");
        public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        public static void Write(string path,object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temp,path,path+".bak",true); else File.Move(temp,path);
        }
        public static T Read<T>(string path) {return Json.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
        public static Config Load() { if(!File.Exists(FilePath)) {var d=Config.Defaults();Save(d);return d;} var c=Read<Config>(FilePath);c.Validate();return c; }
        public static void Save(Config c) {c.Validate();Write(FilePath,c);}
        public static void Log(string message) {try {Directory.CreateDirectory(Dir);File.AppendAllText(Path.Combine(Dir,"events.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8);}catch{}}
    }
    static class Integration {
        public const string MenuKey=@"Software\Classes\DesktopBackground\Shell\LocalResolutionSwitcher";
        public const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string Exe {get {return Application.ExecutablePath;}}
        public static bool Installed {get {using(var k=Registry.CurrentUser.OpenSubKey(MenuKey)) return k!=null;}}
        public static bool AutoStart {get {using(var k=Registry.CurrentUser.OpenSubKey(RunKey)) return k!=null && k.GetValue("LocalResolutionSwitcher")!=null;}}
        public static void SetStartup(bool enabled) {using(var k=Registry.CurrentUser.CreateSubKey(RunKey)) {if(enabled) k.SetValue("LocalResolutionSwitcher","\""+Exe+"\" --tray");else k.DeleteValue("LocalResolutionSwitcher",false);}}
        public static void Install(Config c) {
            using(var k=Registry.CurrentUser.CreateSubKey(MenuKey)) {
                k.SetValue("MUIVerb","屏幕分辨率切换"); k.SetValue("Icon",Exe+",0"); k.SetValue("SubCommands","");
                k.DeleteSubKeyTree("shell",false);
                using(var s=k.CreateSubKey("shell")) {
                    int i=0;foreach(var p in c.Presets) Add(s,(i++).ToString("D3"),p.Label,"--apply "+p.Id);
                    Add(s,"900","恢复日常桌面分辨率","--daily"); Add(s,"999","打开分辨率管理","");
                }
            }
        }
        static void Add(RegistryKey shell,string id,string label,string arg) {using(var k=shell.CreateSubKey(id)) {k.SetValue("MUIVerb",label);using(var cmd=k.CreateSubKey("command")) cmd.SetValue("","\""+Exe+"\" "+arg);}}
        public static void Remove() {Registry.CurrentUser.DeleteSubKeyTree(MenuKey,false);}
    }
    sealed class Operation : IDisposable {
        Mutex mutex; bool owns;
        public Operation() {mutex=new Mutex(false,@"Local\LocalResolutionSwitcher.DisplayOperation");try {owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns){mutex.Dispose();throw new InvalidOperationException("另一个分辨率操作正在进行，请先确认或等待其恢复。");}}
        public void Dispose(){if(owns){mutex.ReleaseMutex();owns=false;}mutex.Dispose();}
    }
    static class Safety {
        public static Process Arm(string device,DevMode original,int seconds) {
            if(File.Exists(Store.Journal)) throw new InvalidOperationException("存在尚未恢复的显示记录，请先使用“恢复上次原始模式”。");
            if(File.Exists(Store.Journal+".ready"))File.Delete(Store.Journal+".ready");
            Store.Write(Store.Journal,new Recovery {Device=device,Original=original,Deadline=DateTime.UtcNow.AddSeconds(seconds).Ticks,Parent=Process.GetCurrentProcess().Id});
            try {
                var p=Process.Start(new ProcessStartInfo(Integration.Exe,"--guard") {UseShellExecute=false,CreateNoWindow=true});
                var sw=Stopwatch.StartNew(); while(sw.ElapsedMilliseconds<4000) {if(File.Exists(Store.Journal+".ready")) return p;if(p.HasExited)break;Thread.Sleep(40);}
                throw new InvalidOperationException("独立恢复进程未就绪，已取消切换。");
            }catch {Disarm();throw;}
        }
        public static void Disarm() {if(File.Exists(Store.Journal)) File.Delete(Store.Journal);if(File.Exists(Store.Journal+".ready")) File.Delete(Store.Journal+".ready");}
        public static void Guard() {
            if(!File.Exists(Store.Journal))return;
            var r=Store.Read<Recovery>(Store.Journal);File.WriteAllText(Store.Journal+".ready",Process.GetCurrentProcess().Id.ToString());
            while(File.Exists(Store.Journal)&&DateTime.UtcNow.Ticks<r.Deadline) {
                try {if(Process.GetProcessById(r.Parent).HasExited)break;}catch(ArgumentException){break;}Thread.Sleep(100);
            }
            if(!File.Exists(Store.Journal)){if(File.Exists(Store.Journal+".ready"))File.Delete(Store.Journal+".ready");return;}
            try {Display.Restore(r.Device,r.Original);Store.Log("WATCHDOG_RESTORED "+r.Original);Disarm();}catch(Exception ex){Store.Log("WATCHDOG_FAILED "+ex.Message);}
        }
        public static void Recover() {
            if(!File.Exists(Store.Journal))throw new InvalidOperationException("没有待恢复记录。");
            var r=Store.Read<Recovery>(Store.Journal); Display.Restore(r.Device,r.Original);Disarm();
        }
        public static string Apply(IWin32Window owner,string device,Preset p) {
            using(var op=new Operation()) {
                var original=Display.Current(device);var target=Display.Select(device,p);int test=Display.Set(device,target,true);
                if(test!=0)throw new InvalidOperationException("Windows 测试未通过："+Display.Error(test));
                if(Display.Same(original,target))return "当前已是 "+original;
                using(var guard=Arm(device,original,22)) {
                    try {
                        int result=Display.Set(device,target,false);if(result!=0)throw new InvalidOperationException("切换失败："+Display.Error(result));
                        var actual=Display.Current(device);if(!Display.Same(actual,target))throw new InvalidOperationException("系统实际模式与目标不一致。");
                        Store.Log("APPLIED "+device+" "+actual);
                        using(var confirm=new ConfirmForm(actual)) {
                            if(confirm.ShowDialog(owner)==DialogResult.OK) {
                                // Do not accept after the watchdog deadline or after an external display change.
                                if(!File.Exists(Store.Journal)||!Display.Same(Display.Current(device),target))throw new InvalidOperationException("确认已超时或模式已变化。");
                                var r=Store.Read<Recovery>(Store.Journal);if(DateTime.UtcNow.Ticks>=r.Deadline)throw new InvalidOperationException("确认已超时。");
                                Disarm();guard.WaitForExit(1500);Store.Log("KEPT "+actual);return "已保留："+Display.Current(device);
                            }
                        }
                        Display.Restore(device,original);Disarm();guard.WaitForExit(1500);return "已恢复原始模式："+Display.Current(device);
                    }catch {
                        try {Display.Restore(device,original);Disarm();guard.WaitForExit(1500);}catch(Exception ex){Store.Log("RECOVERY_PENDING "+ex.Message);}
                        throw;
                    }
                }
            }
        }
    }
    sealed class ConfirmForm : Form {
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=200};
        readonly Label countdown=new Label {Dock=DockStyle.Top,Height=42,TextAlign=ContentAlignment.MiddleCenter};
        readonly DateTime end=DateTime.UtcNow.AddSeconds(15);
        public ConfirmForm(DevMode actual) {
            Text="确认显示效果";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(470,185);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;TopMost=true;
            var detail=new Label {Text="当前实际模式："+actual,Dock=DockStyle.Top,Height=48,TextAlign=ContentAlignment.MiddleCenter};
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=55,FlowDirection=FlowDirection.RightToLeft};
            var revert=new Button {Text="恢复原模式",Width=130,Height=34,DialogResult=DialogResult.Cancel};
            var keep=new Button {Text="保留此设置",Width=130,Height=34,DialogResult=DialogResult.OK};buttons.Controls.Add(revert);buttons.Controls.Add(keep);Controls.Add(countdown);Controls.Add(detail);Controls.Add(buttons);CancelButton=revert;ActiveControl=revert;
            timer.Tick+=(s,e)=>{int n=(int)Math.Ceiling((end-DateTime.UtcNow).TotalSeconds);countdown.Text=n+" 秒内未确认将自动恢复";if(n<=0){DialogResult=DialogResult.Cancel;Close();}};timer.Start();
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
    sealed class PresetEditor : Form {
        TextBox name=new TextBox();NumericUpDown width=Number(320,16384),height=Number(200,16384),hz=Number(0,1000);
        public Preset Result;
        static NumericUpDown Number(int min,int max){return new NumericUpDown {Minimum=min,Maximum=max,Dock=DockStyle.Fill};}
        public PresetEditor(Preset old) {
            Text=old==null?"添加预设":"编辑预设";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(420,265);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
            var grid=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=2,RowCount=5};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            string[] labels={"预设名称","宽度（像素）","高度（像素）","刷新率（0 = 自动）"};Control[] inputs={name,width,height,hz};for(int i=0;i<4;i++){grid.RowStyles.Add(new RowStyle(SizeType.Absolute,42));grid.Controls.Add(new Label {Text=labels[i],AutoSize=true,Anchor=AnchorStyles.Left},0,i);grid.Controls.Add(inputs[i],1,i);}
            var ok=new Button {Text="保存",Dock=DockStyle.Fill};grid.Controls.Add(ok,1,4);Controls.Add(grid);
            name.Text=old==null?"自定义":old.Name;width.Value=old==null?1920:old.Width;height.Value=old==null?1080:old.Height;hz.Value=old==null?0:old.Hz;
            ok.Click+=(s,e)=>{try {Result=new Preset {Name=name.Text.Trim(),Width=(int)width.Value,Height=(int)height.Value,Hz=(int)hz.Value};if(old!=null)Result.Id=old.Id;Result.Validate();DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法保存");}};
        }
    }
    sealed class MainForm : Form {
        Config config;ComboBox devices=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        Label current=new Label {Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
        ListView presets=new ListView {View=View.Details,FullRowSelect=true,MultiSelect=false,Dock=DockStyle.Fill,HideSelection=false};
        ListBox modes=new ListBox {Dock=DockStyle.Fill,HorizontalScrollbar=true};
        Label status=new Label {Dock=DockStyle.Fill,Text="就绪",AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};
        NotifyIcon tray;ContextMenuStrip menu=new ContextMenuStrip();bool quitting,busy;readonly string[] args;
        System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer {Interval=2000};
        string SelectedDevice {get {return devices.SelectedItem==null?null:((Device)devices.SelectedItem).name;}}
        public MainForm(Config c,string[] arguments) {
            config=c;args=arguments;Text="屏幕分辨率切换";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(1000,650);MinimumSize=new Size(850,580);StartPosition=FormStartPosition.CenterScreen;Icon=SystemIcons.Application;AutoScaleMode=AutoScaleMode.Dpi;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=7};
            int[] rows={42,52,38,0,50,76,36};for(int i=0;i<rows.Length;i++)layout.RowStyles.Add(rows[i]==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,rows[i]));
            layout.Controls.Add(devices,0,0);current.Font=new Font(Font.FontFamily,17,FontStyle.Bold);layout.Controls.Add(current,0,1);
            var top=Flow();top.Controls.Add(new Label {Text="分辨率预设",AutoSize=true,Margin=new Padding(0,8,20,0)});Button(top,"添加",()=>Edit(false));Button(top,"编辑",()=>Edit(true));Button(top,"删除",Delete);Button(top,"设为日常",SetDaily);Button(top,"刷新",ReloadDevices);layout.Controls.Add(top,0,2);
            var split=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));
            presets.Columns.Add("名称",145);presets.Columns.Add("分辨率",125);presets.Columns.Add("刷新率",105);presets.Columns.Add("状态",160);
            split.Controls.Add(presets,0,0);var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};right.RowStyles.Add(new RowStyle(SizeType.Absolute,30));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.Controls.Add(new Label {Text="可用模式（双击添加预设）",AutoSize=true},0,0);right.Controls.Add(modes,0,1);split.Controls.Add(right,1,0);layout.Controls.Add(split,0,3);
            var actions=Flow();Button(actions,"应用选中预设",()=>Apply(Selected()));Button(actions,"恢复日常桌面",()=>Apply(config.Presets.First(p=>p.Id==config.DefaultId)));Button(actions,"恢复上次原始模式",()=>Run(()=>{using(var op=new Operation())Safety.Recover();return "已恢复原始模式";}));layout.Controls.Add(actions,0,4);
            var settings=Flow();var close=new CheckBox {Text="关闭窗口时留在托盘",Checked=config.CloseToTray,AutoSize=true,Margin=new Padding(3,10,18,3)};close.CheckedChanged+=(s,e)=>Run(()=>{config.CloseToTray=close.Checked;Store.Save(config);return "已保存关闭行为";});settings.Controls.Add(close);
            var startup=new CheckBox {Text="开机启动",Checked=Integration.AutoStart,AutoSize=true,Margin=new Padding(3,10,18,3)};startup.CheckedChanged+=(s,e)=>Run(()=>{Integration.SetStartup(startup.Checked);return "已更新开机启动";});settings.Controls.Add(startup);
            Button(settings,"安装/更新桌面菜单",()=>Run(()=>{Integration.Install(config);return "已安装：桌面右键 → 显示更多选项 → 屏幕分辨率切换";}));Button(settings,"移除桌面菜单",()=>Run(()=>{Integration.Remove();return "已移除桌面菜单";}));Button(settings,"退出软件",()=>{quitting=true;Close();});layout.Controls.Add(settings,0,5);layout.Controls.Add(status,0,6);Controls.Add(layout);
            devices.SelectedIndexChanged+=(s,e)=>Run(()=>{config.Device=SelectedDevice;Store.Save(config);RefreshModes();return "显示器已选择";});
            presets.DoubleClick+=(s,e)=>Apply(Selected());modes.DoubleClick+=(s,e)=>{if(modes.SelectedItem is DevMode){var m=(DevMode)modes.SelectedItem;using(var ed=new PresetEditor(new Preset {Name="自定义",Width=(int)m.width,Height=(int)m.height,Hz=(int)m.hz})){if(ed.ShowDialog(this)==DialogResult.OK)Run(()=>{ed.Result.Id=Guid.NewGuid().ToString("N");config.Presets.Add(ed.Result);Save();return "已添加预设";});}}};
            tray=new NotifyIcon {Icon=Icon,Text="屏幕分辨率切换",Visible=args.Length==0||args[0]=="--tray",ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>ShowManager();tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left){BuildMenu();menu.Show(Cursor.Position);}};menu.Opening+=(s,e)=>BuildMenu();
            refresh.Tick+=(s,e)=>{if(!busy)try {current.Text="当前实际模式  "+Display.Current(SelectedDevice);}catch(Exception ex){status.Text=ex.Message;}};refresh.Start();
            FormClosing+=(s,e)=>{if(!quitting && args.Length==0 && config.CloseToTray){e.Cancel=true;Hide();tray.ShowBalloonTip(1500,"已留在托盘","通过托盘菜单打开管理或退出。",ToolTipIcon.Info);}};
            Shown+=(s,e)=>{if(args.Length>0 && args[0]=="--tray")Hide();else if(args.Length>0&&(args[0]=="--apply"||args[0]=="--daily")){BeginInvoke(new Action(()=>{try {var p=args[0]=="--daily"?config.Presets.First(x=>x.Id==config.DefaultId):config.Presets.FirstOrDefault(x=>args.Length>1&&x.Id==args[1]);if(p==null)throw new InvalidOperationException("预设已删除，请更新桌面菜单。");Apply(p);}catch(Exception ex){MessageBox.Show(this,ex.Message,"操作失败");}finally{quitting=true;Close();}}));}};
            ReloadDevices();
        }
        static FlowLayoutPanel Flow(){return new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true};}
        static void Button(Control parent,string text,Action action){var b=new Button {Text=text,AutoSize=true,MinimumSize=new Size(76,33),Margin=new Padding(3,3,8,3)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(parent,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};parent.Controls.Add(b);}
        Preset Selected(){if(presets.SelectedItems.Count==0)throw new InvalidOperationException("请先选择预设。");return (Preset)presets.SelectedItems[0].Tag;}
        void ShowManager(){Show();WindowState=FormWindowState.Normal;Activate();}
        void BuildMenu(){menu.Items.Clear();foreach(var p in config.Presets){var captured=p;menu.Items.Add(p.Label,null,(s,e)=>Apply(captured));}menu.Items.Add(new ToolStripSeparator());menu.Items.Add("恢复日常桌面",null,(s,e)=>Apply(config.Presets.First(p=>p.Id==config.DefaultId)));menu.Items.Add("打开分辨率管理",null,(s,e)=>ShowManager());menu.Items.Add("退出",null,(s,e)=>{quitting=true;Close();});}
        void Run(Func<string> action){if(busy)return;try {busy=true;status.Text=action();}catch(Exception ex){status.Text=ex.Message;Store.Log("ERROR "+ex);MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;}}
        void Apply(Preset p){Run(()=>{string result=Safety.Apply(this,SelectedDevice,p);RefreshModes();return result;});}
        void Edit(bool edit){Run(()=>{var p=edit?Selected():null;using(var ed=new PresetEditor(p)){if(ed.ShowDialog(this)==DialogResult.OK){if(edit)config.Presets[config.Presets.FindIndex(x=>x.Id==p.Id)]=ed.Result;else config.Presets.Add(ed.Result);Save();}}return "预设管理就绪";});}
        void Delete(){Run(()=>{var p=Selected();if(p.Id==config.DefaultId)throw new InvalidOperationException("请先将其他预设设为日常，再删除此预设。");if(MessageBox.Show(this,"删除预设“"+p.Name+"”？","删除预设",MessageBoxButtons.YesNo)==DialogResult.Yes){config.Presets.Remove(p);Save();}return "预设管理就绪";});}
        void SetDaily(){Run(()=>{config.DefaultId=Selected().Id;Save();return "已设置日常桌面预设";});}
        void Save(){Store.Save(config);if(Integration.Installed)Integration.Install(config);RefreshModes();}
        void ReloadDevices(){Run(()=>{var list=Display.Devices();if(list.Count==0)throw new InvalidOperationException("未找到活动显示器。");devices.Items.Clear();foreach(var d in list)devices.Items.Add(d);devices.SelectedIndex=Math.Max(0,list.FindIndex(d=>d.name==config.Device));config.Device=SelectedDevice;Store.Save(config);RefreshModes();return "就绪 · 切换后有 15 秒确认时间，超时自动恢复";});}
        void RefreshModes(){var currentMode=Display.Current(SelectedDevice);current.Text="当前实际模式  "+currentMode;var available=Display.Modes(SelectedDevice);presets.Items.Clear();foreach(var p in config.Presets){bool supported=available.Any(m=>m.width==p.Width&&m.height==p.Height&&m.bpp==currentMode.bpp&&m.orientation==currentMode.orientation&&(p.Hz==0||m.hz==p.Hz));var row=new ListViewItem(p.Name+(p.Id==config.DefaultId?"（日常）":""));row.SubItems.Add(p.Width+" × "+p.Height);row.SubItems.Add(p.Hz==0?"自动保留 / 最高":p.Hz+" Hz");row.SubItems.Add(supported?"支持，应用前测试":"驱动未提供");row.Tag=p;if(!supported)row.ForeColor=Color.Firebrick;presets.Items.Add(row);}if(presets.Items.Count>0)presets.Items[0].Selected=true;modes.Items.Clear();foreach(var m in available.GroupBy(x=>x.width+"/"+x.height+"/"+x.hz).Select(g=>g.First()).OrderByDescending(x=>x.width).ThenByDescending(x=>x.height).ThenByDescending(x=>x.hz))modes.Items.Add(m);}
        protected override void Dispose(bool disposing){if(disposing){refresh.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}menu.Dispose();}base.Dispose(disposing);}
    }
    static class Program {
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        [STAThread]static int Main(string[] args) {
            try {
                SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length>0&&args[0]=="--guard"){Safety.Guard();return 0;}
                if(args.Length>1&&(args[0]=="--probe"||args[0]=="--exercise")){Diagnostics(args[1],args[0]=="--exercise");return 0;}
                if(args.Length>0&&args[0]=="--install-menu"){Integration.Install(Store.Load());return 0;}
                if(args.Length>0&&args[0]=="--remove-menu"){Integration.Remove();return 0;}
                if(args.Length>0&&args[0]=="--recover"){using(var op=new Operation())Safety.Recover();return 0;}
                Application.Run(new MainForm(Store.Load(),args));return 0;
            }catch(Exception ex){Store.Log("FATAL "+ex);if(args.Length>1&&(args[0]=="--probe"||args[0]=="--exercise"))File.WriteAllText(args[1]+".error",ex.ToString());else if(args.Length==0||args[0]!="--guard")MessageBox.Show(ex.Message,"分辨率工具无法继续",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        static void Diagnostics(string path,bool exercise) {
            var report=new StringBuilder();report.AppendLine("LocalResolutionSwitcher "+DateTime.Now.ToString("s"));report.AppendLine("DEVMODE bytes="+Marshal.SizeOf(typeof(DevMode)));
            var config=Store.Load();var roundtrip=Store.Json.Deserialize<Config>(Store.Json.Serialize(config));roundtrip.Validate();report.AppendLine("CONFIG_ROUNDTRIP PASS");
            foreach(var d in Display.Devices()) {
                var original=Display.Current(d.name);report.AppendLine("DEVICE "+d);report.AppendLine("ORIGINAL "+original);var modes=Display.Modes(d.name);report.AppendLine("ENUMERATED "+modes.Count);foreach(var m in modes)report.AppendLine("MODE "+m);
                foreach(var p in config.Presets) {
                    try {var target=Display.Select(d.name,p);int test=Display.Set(d.name,target,true);report.AppendLine("TEST "+p.Name+" => "+target+" : "+Display.Error(test));
                        if(exercise&&test==0)using(var op=new Operation())using(var guard=Safety.Arm(d.name,original,12)) {
                            try {int r=Display.Set(d.name,target,false);Thread.Sleep(750);var actual=Display.Current(d.name);report.AppendLine("REAL_APPLY "+p.Name+" code="+r+" actual="+actual+" match="+Display.Same(actual,target));if(r!=0||!Display.Same(actual,target))throw new Exception("实际切换验证失败");}
                            finally {Display.Restore(d.name,original);Safety.Disarm();guard.WaitForExit(2000);report.AppendLine("RESTORE "+Display.Current(d.name)+" match="+Display.Same(Display.Current(d.name),original));}
                        }
                    }catch(InvalidOperationException ex){report.AppendLine("UNAVAILABLE "+p.Name+" "+ex.Message);}
                }
                if(exercise)using(var op=new Operation()) {
                    var candidate=modes.FirstOrDefault(m=>m.width!=original.width&&m.height!=original.height&&m.bpp==original.bpp&&m.orientation==original.orientation);
                    if(candidate.width>0)using(var guard=Safety.Arm(d.name,original,4)) {
                        try {int r=Display.Set(d.name,candidate,false);if(r!=0)throw new Exception("Watchdog test apply failed");guard.WaitForExit(7000);bool restored=Display.Same(Display.Current(d.name),original);report.AppendLine("WATCHDOG_TIMEOUT restored="+restored+" exited="+guard.HasExited);if(!restored)throw new Exception("Watchdog recovery failed");}
                        finally {Display.Restore(d.name,original);Safety.Disarm();}
                    }
                }
                report.AppendLine("FINAL "+Display.Current(d.name));
            }
            Store.Write(Path.Combine(Store.Dir,"config-roundtrip-test.json"),roundtrip);var loaded=Store.Read<Config>(Path.Combine(Store.Dir,"config-roundtrip-test.json"));loaded.Validate();report.AppendLine("CONFIG_DISK_RELOAD "+(loaded.Presets.Count==config.Presets.Count));File.WriteAllText(path,report.ToString(),Encoding.UTF8);
        }
    }
}
