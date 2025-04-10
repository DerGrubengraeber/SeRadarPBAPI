using System;
using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Utils;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;

namespace GrubenRadarApi
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public partial class RadarApiMod : MySessionComponentBase
    {
        Dictionary<string, Delegate> NerdRadarAPI = null;
        public PBInterface PBInterface;
        public const string TerminalPropertyId = "RadarAPI";
        public override void LoadData()
        {
            MyLog.Default.WriteLine("Loading RadarAPI...");
            try
            {
                PBInterface = new PBInterface(this, "GrubenRadarAPI");
                SetupApi();
                RegisterRadarApi();
            }
            catch(Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"{e.Message}\n{e.StackTrace}");
            }
        }
        protected override void UnloadData()
        {
            UnregisterRadarApi();
            base.UnloadData();
            PBInterface?.Dispose();
        }
   
    }
}
