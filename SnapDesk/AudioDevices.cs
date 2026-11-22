using System.Runtime.InteropServices;

namespace SnapDesk;

internal static class AudioDevices
{
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out nint collection);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
        [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);
        [PreserveSig]int RegisterEndpointNotificationCallback(nint callback);
        [PreserveSig]int UnregisterEndpointNotificationCallback(nint callback);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig]int Activate(ref Guid iid,uint context,nint parameters,out nint instance);
        [PreserveSig]int OpenPropertyStore(uint access,out nint properties);
        [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetState(out uint state);
    }
    internal static string? DefaultEndpoint(bool input)
    {
        IMMDeviceEnumerator? enumerator=null;IMMDevice? device=null;
        try
        {
            var type=Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;
            enumerator=(IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            if(enumerator.GetDefaultAudioEndpoint(input?1:0,0,out device)!=0||device==null)return null;
            return device.GetId(out string id)==0?id:null;
        }
        catch(COMException){return null;}
        finally{if(device!=null)Marshal.ReleaseComObject(device);if(enumerator!=null)Marshal.ReleaseComObject(enumerator);}
    }
}
