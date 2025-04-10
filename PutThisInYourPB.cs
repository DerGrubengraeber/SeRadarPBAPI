
public class Program : MyGridProgram
{
	
	

		GrubenRadarAPI RadarAPI;

        public Program()
        {

			RadarAPI = new GrubenRadarAPI(this);
        }

        public void Main(string argument, UpdateType updateType)
        {
			if (!RadarAPI.ModDetected)
    {
        Echo("Radar API not detected.");
        return;
    }


    var targets = RadarAPI.GetDetectedRadarTargets(this.Me);

    Echo($"Detected {targets.Count} targets:");
    foreach (var target in targets)
    {
        Echo($"- {target.Name} ({target.Type})");
    }
			
        }
		
		
		public class GrubenRadarAPI
{
    public readonly bool ModDetected;

    Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>> _gtrt;
    IMyProgrammableBlock _pb;

    public GrubenRadarAPI(MyGridProgram program)
    {
        if (program == null)
            throw new Exception("Pass `this` into the API constructor.");

        _pb = program.Me;

        var prop = _pb.GetProperty("GrubenRadarAPI");
        if (prop == null)
            return;

        var methods = prop.As<IReadOnlyDictionary<string, Delegate>>().GetValue(_pb);
        if (methods == null)
            return;

        Delegate rawDelegate;
        if (!methods.TryGetValue("GetDetectedRadarTargets", out rawDelegate))
            return;

        _gtrt = rawDelegate as Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>>;
        ModDetected = _gtrt != null;
    }

    public List<MyDetectedEntityInfo> GetDetectedRadarTargets(IMyProgrammableBlock pb)
    {
        if (_gtrt == null || pb == null)
            return new List<MyDetectedEntityInfo>();

        return _gtrt.Invoke(pb);
    }
}



}
