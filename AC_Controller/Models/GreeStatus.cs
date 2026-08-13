namespace AC_Controller.Models;

public sealed class GreeStatus
{
    public int Pow { get; init; }

    public int Mod { get; init; }

    public int SetTem { get; init; }

    public int WdSpd { get; init; }

    public int Air { get; init; }

    public int Blo { get; init; }

    public int Health { get; init; }

    public int SwhSlp { get; init; }

    public int Lig { get; init; }

    public int SwingLfRig { get; init; }

    public int SwUpDn { get; init; }

    public int Quiet { get; init; }

    public int Tur { get; init; }

    public int StHt { get; init; }

    public int TemUn { get; init; }

    public int HeatCoolType { get; init; }

    public int TemRec { get; init; }

    public int SvSt { get; init; }

    public bool IsPoweredOn => Pow == 1;
}