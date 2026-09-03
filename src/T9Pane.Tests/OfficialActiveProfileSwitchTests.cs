using System.Runtime.InteropServices;
using T9Pane.Services;

namespace T9Pane.Tests;

public class OfficialActiveProfileSwitchTests
{
    [Fact]
    public void GetActiveProfile_follows_official_activate_t9_and_mspy()
    {
        Assert.True(TsfLayoutSelection.TryGetActive(out var beforeClsid, out _));
        try
        {
            Assert.Equal(0, TsfTestActivate.ActivateT9());
            Assert.True(Spin(TsfLayoutSelection.IsT9Selected, want: true));
            Assert.Equal(0, TsfTestActivate.ActivateMspy());
            Assert.True(Spin(TsfLayoutSelection.IsT9Selected, want: false));
        }
        finally
        {
            if (beforeClsid == OfficialT9ProfilePolicy.T9Clsid)
            {
                TsfTestActivate.ActivateT9();
            }
            else
            {
                TsfTestActivate.ActivateMspy();
            }
        }
    }

    private static bool Spin(Func<bool> read, bool want)
    {
        for (var i = 0; i < 20; i++)
        {
            if (read() == want)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }
}

internal static class TsfTestActivate
{
    private const uint Flags = 0x20000000 | 0x00000001 | 0x00000004;
    private static readonly Guid ClsidProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid IidMgr = new("71C6E74C-0F28-11D8-A82A-00065B84435C");
    private static readonly Guid Mspy = new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    private static readonly Guid MspyProfile = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    public static int ActivateT9() =>
        Activate(OfficialT9ProfilePolicy.T9Clsid, OfficialT9ProfilePolicy.T9Profile);

    public static int ActivateMspy() => Activate(Mspy, MspyProfile);

    private static int Activate(Guid clsid, Guid profile)
    {
        var hr = unchecked((int)0x80004005);
        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromCLSID(ClsidProfiles, true);
                var mgr = (ITfInputProcessorProfileMgr)Activator.CreateInstance(type!)!;
                var c = clsid;
                var p = profile;
                hr = mgr.ActivateProfile(1, 0x0804, ref c, ref p, IntPtr.Zero, Flags);
            }
            finally
            {
                done.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        thread.Join();
        return hr;
    }
}
