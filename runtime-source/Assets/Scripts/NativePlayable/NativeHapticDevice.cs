using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public interface INativeHapticDevice : IDisposable
    {
        string Name { get; }
        string Status { get; }
        bool Connected { get; }
        bool Write(NativeHapticOutput output);
        void Stop();
    }

    public static class NativeHapticDevices
    {
        [Serializable] sealed class HIDIdentity { public int vendorId,productId; }
        public static bool IsDS4(Gamepad pad)
        {
            if(pad.layout.IndexOf("DualShock4",StringComparison.OrdinalIgnoreCase)>=0)return true;
            var id=JsonUtility.FromJson<HIDIdentity>(pad.description.capabilities ?? "{}");
            return id!=null && id.vendorId==0x054c && (id.productId==0x05c4 || id.productId==0x09cc);
        }
        public static INativeHapticDevice Open(Gamepad pad)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (IsDS4(pad))
            {
                var native = NativeMacDS4Haptics.Open(pad);
                if (native != null) return native;
                return new Unavailable(pad,"DS4 设备匹配或打开失败");
            }
            if (pad.layout.IndexOf("XInput",StringComparison.OrdinalIgnoreCase) >= 0)
                return new Unavailable(pad,"macOS Xbox 模式振动待适配；请切换 DS4 模式");
            return new Unavailable(pad,"此连接模式振动待适配；请切换 DS4 模式");
#else
            return new UnityRumble(pad);
#endif
        }
        public static INativeHapticDevice Audit() => new AuditDevice();
        sealed class AuditDevice : INativeHapticDevice
        {
            public string Name => "Audit";
            public string Status => "自动校验 · 实机输出关闭";
            public bool Connected => true;
            public bool Write(NativeHapticOutput output) => true;
            public void Stop() { } public void Dispose() { }
        }
        sealed class Unavailable : INativeHapticDevice
        {
            readonly Gamepad pad; public string Name => pad.description.product; public string Status { get; }
            public bool Connected => false;
            public Unavailable(Gamepad pad,string status) { this.pad=pad; Status=status; }
            public bool Write(NativeHapticOutput output) => false;
            public void Stop() { } public void Dispose() { }
        }
        sealed class UnityRumble : INativeHapticDevice
        {
            readonly Gamepad pad;
            public string Name => pad.description.product ?? pad.layout;
            public string Status { get; private set; } = "Unity 双马达";
            public bool Connected => pad.added && pad.enabled;
            public UnityRumble(Gamepad pad) { this.pad=pad; }
            public bool Write(NativeHapticOutput output)
            {
                if (!Connected) return false;
                pad.SetMotorSpeeds(output.low,output.high); return true;
            }
            public void Stop() { if (pad.added) pad.ResetHaptics(); }
            public void Dispose() => Stop();
        }
    }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    public sealed class NativeMacDS4Haptics : INativeHapticDevice
    {
        const string IOKit="/System/Library/Frameworks/IOKit.framework/IOKit";
        const string Core="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const uint UTF8=0x08000100;
        [DllImport(IOKit)] static extern IntPtr IOHIDManagerCreate(IntPtr allocator,uint options);
        [DllImport(IOKit)] static extern void IOHIDManagerSetDeviceMatching(IntPtr manager,IntPtr match);
        [DllImport(IOKit)] static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);
        [DllImport(IOKit)] static extern IntPtr IOHIDDeviceGetProperty(IntPtr device,IntPtr key);
        [DllImport(IOKit)] static extern int IOHIDDeviceOpen(IntPtr device,uint options);
        [DllImport(IOKit)] static extern int IOHIDDeviceClose(IntPtr device,uint options);
        [DllImport(IOKit)] static extern int IOHIDDeviceSetReport(IntPtr device,int type,IntPtr id,byte[] report,IntPtr length);
        [DllImport(Core)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator,string value,uint encoding);
        [DllImport(Core)] static extern bool CFStringGetCString(IntPtr value,StringBuilder buffer,IntPtr size,uint encoding);
        [DllImport(Core)] static extern bool CFNumberGetValue(IntPtr value,int type,out int result);
        [DllImport(Core)] static extern IntPtr CFSetGetCount(IntPtr set);
        [DllImport(Core)] static extern void CFSetGetValues(IntPtr set,[Out] IntPtr[] values);
        [DllImport(Core)] static extern IntPtr CFRetain(IntPtr value);
        [DllImport(Core)] static extern void CFRelease(IntPtr value);
        [Serializable] sealed class HIDInfo { public int vendorId,productId; }
        readonly Gamepad pad;
        IntPtr device;
        readonly bool bluetooth;
        readonly byte[] report;
        public string Name => pad.description.product;
        public string Status { get; private set; }
        public bool Connected => device != IntPtr.Zero && pad.added && pad.enabled;
        public int Writes { get; private set; }
        NativeMacDS4Haptics(Gamepad pad,IntPtr device,bool bluetooth)
        {
            this.pad=pad; this.device=device; this.bluetooth=bluetooth;
            report=new byte[bluetooth ? 78 : 32]; Status=bluetooth ? "DS4 蓝牙双马达" : "DS4 USB 双马达";
        }
        static IntPtr Property(IntPtr device,string name)
        {
            var key=CFStringCreateWithCString(IntPtr.Zero,name,UTF8);
            try { return IOHIDDeviceGetProperty(device,key); } finally { CFRelease(key); }
        }
        static int Number(IntPtr device,string name)
        { var v=Property(device,name); return v!=IntPtr.Zero && CFNumberGetValue(v,3,out int n) ? n : 0; }
        static string Text(IntPtr device,string name)
        {
            var v=Property(device,name); if (v==IntPtr.Zero) return "";
            var b=new StringBuilder(256); return CFStringGetCString(v,b,(IntPtr)256,UTF8) ? b.ToString() : "";
        }
        public static NativeMacDS4Haptics Open(Gamepad pad)
        {
            IntPtr manager=IntPtr.Zero,set=IntPtr.Zero;
            try
            {
                var info=JsonUtility.FromJson<HIDInfo>(pad.description.capabilities ?? "{}");
                int vendor=info != null && info.vendorId!=0 ? info.vendorId : 0x054c;
                int product=info != null && info.productId!=0 ? info.productId : 0x05c4;
                manager=IOHIDManagerCreate(IntPtr.Zero,0); IOHIDManagerSetDeviceMatching(manager,IntPtr.Zero);
                set=IOHIDManagerCopyDevices(manager); if (set==IntPtr.Zero) return null;
                var values=new IntPtr[CFSetGetCount(set).ToInt32()]; CFSetGetValues(set,values);
                IntPtr selected=IntPtr.Zero,serialMatch=IntPtr.Zero; int count=0,serialCount=0;
                foreach (var d in values)
                {
                    if (Number(d,"VendorID")!=vendor || Number(d,"ProductID")!=product || Number(d,"PrimaryUsagePage")!=1 || Number(d,"PrimaryUsage")!=5) continue;
                    string serial=pad.description.serial;
                    if (!string.IsNullOrEmpty(serial) && string.Equals(serial,Text(d,"SerialNumber"),StringComparison.OrdinalIgnoreCase)) { serialMatch=d;serialCount++; }
                    selected=d; count++;
                }
                // Multiple identical endpoints require a unique serial match to preserve ownership.
                if(count>1 && serialCount==1) { selected=serialMatch;count=1; }
                if (count!=1 || IOHIDDeviceOpen(selected,0)!=0) return null;
                CFRetain(selected);
                return new NativeMacDS4Haptics(pad,selected,Text(selected,"Transport").IndexOf("Bluetooth",StringComparison.OrdinalIgnoreCase)>=0);
            }
            catch (Exception e) { Debug.LogWarning("NATIVE_HAPTICS_MAC_OPEN "+e.GetType().Name); return null; }
            finally { if(set!=IntPtr.Zero)CFRelease(set); if(manager!=IntPtr.Zero)CFRelease(manager); }
        }
        public static byte[] Packet(bool bluetooth,float low,float high)
        {
            var b=new byte[bluetooth ? 78 : 32]; FillPacket(b,bluetooth,low,high); return b;
        }
        static void FillPacket(byte[] b,bool bluetooth,float low,float high)
        {
            Array.Clear(b,0,b.Length);
            int offset=bluetooth ? 6 : 4;
            b[0]=bluetooth ? (byte)0x11 : (byte)0x05;
            if(bluetooth) { b[1]=0xc4; b[3]=1; } else b[1]=1;
            b[offset]=(byte)Mathf.FloorToInt(Mathf.Clamp01(high)*255+.5f);
            b[offset+1]=(byte)Mathf.FloorToInt(Mathf.Clamp01(low)*255+.5f);
            if (bluetooth)
            {
                // The HIDP output header participates in the DS4 Bluetooth checksum.
                uint crc=0xffffffff;
                for(int i=-1;i<b.Length-4;i++)
                {
                    crc^=i<0 ? (uint)0xa2 : b[i];
                    for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0 ? 0xedb88320u : 0);
                }
                crc=~crc;
                for(int i=0;i<4;i++)b[b.Length-4+i]=(byte)(crc>>(8*i));
            }
        }
        public bool Write(NativeHapticOutput output)
        {
            if (!Connected) return false;
            FillPacket(report,bluetooth,output.low,output.high);
            int result=IOHIDDeviceSetReport(device,1,(IntPtr)report[0],report,(IntPtr)report.Length);
            if (result!=0) { Status="DS4 输出失败 0x"+unchecked((uint)result).ToString("x8"); return false; }
            Writes++; return true;
        }
        public void Stop() { if(Connected)Write(default); }
        public void Dispose()
        {
            if(device==IntPtr.Zero)return; Stop(); IOHIDDeviceClose(device,0); CFRelease(device); device=IntPtr.Zero;
        }
    }
#endif
}
