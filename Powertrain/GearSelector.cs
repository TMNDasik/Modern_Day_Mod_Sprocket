using System;
using UnityEngine;

namespace Powertrain;




internal sealed class GearSelector
{
    public bool Manual;
    public int DriveCount = 1, ReverseCount = 1;
    public int Pos;
    public bool Initialized;

    public float RawThrottle;      
    public float Velocity;         
    public float Rev;              
    public int ActiveGear = -1;    
    public int ActiveMode;         

    
    public int Version;
    public int LastStep;
    public float ShiftTime = -10f;
    public float BlockedTime = -10f;

    
    public bool SyncPending;
    public IntPtr TransmissionPtr;

    
    public bool NoDriver;
    public bool DriverLogged;   

    public int Count => Manual ? ReverseCount + 1 + DriveCount : 3;
    public int NeutralPos => Manual ? ReverseCount : 1;
    public int Dir(int pos) => pos > NeutralPos ? 1 : pos < NeutralPos ? -1 : 0;
    public int Direction => Dir(Pos);

    
    public int GearIndex => Pos > NeutralPos ? Pos - NeutralPos - 1 : Pos < NeutralPos ? NeutralPos - Pos - 1 : 0;

    public string Label(int pos)
    {
        if (!Manual) return pos == 2 ? "D" : pos == 1 ? "N" : "R";
        if (pos == NeutralPos) return "N";
        if (pos > NeutralPos) return (pos - NeutralPos).ToString();
        return ReverseCount == 1 ? "R" : "R" + (NeutralPos - pos);
    }

    
    public string StatusText()
    {
        var dir = Direction;
        if (dir == 0) return "GEAR N";
        if (Manual) return dir > 0 ? "GEAR F" + (GearIndex + 1) : "GEAR R" + (GearIndex + 1);
        if (dir > 0) return ActiveMode == 1 && ActiveGear >= 0 ? "GEAR D" + (ActiveGear + 1) : "GEAR D";
        return ActiveMode == 4 && ActiveGear >= 0 ? "GEAR R" + (ActiveGear + 1) : "GEAR R";
    }

    
    public void Configure(bool manual, int driveCount, int reverseCount)
    {
        driveCount = Math.Max(1, driveCount);
        reverseCount = Math.Max(1, reverseCount);
        if (Initialized && manual == Manual && driveCount == DriveCount && reverseCount == ReverseCount) return;
        var dir = Initialized ? Direction : 0;
        var gear = Initialized ? GearIndex : 0;
        Manual = manual;
        DriveCount = driveCount;
        ReverseCount = reverseCount;
        Pos = dir == 0 ? NeutralPos
            : !Manual ? (dir > 0 ? 2 : 0)
            : dir > 0 ? NeutralPos + 1 + Math.Min(gear, DriveCount - 1)
            : NeutralPos - 1 - Math.Min(gear, ReverseCount - 1);
        Initialized = true;
        Version++;
    }

    
    
    public void SyncTo(int activeMode, int activeGear, float velocity)
    {
        int pos;
        if (Math.Abs(velocity) < 0.5f || activeMode == 2) pos = NeutralPos;
        else if (activeMode == 1) pos = Manual ? NeutralPos + 1 + Math.Clamp(activeGear, 0, DriveCount - 1) : 2;
        else if (activeMode == 4) pos = Manual ? NeutralPos - 1 - Math.Clamp(activeGear, 0, ReverseCount - 1) : 0;
        else pos = NeutralPos;
        if (pos == Pos) return;
        LastStep = Math.Sign(pos - Pos);
        Pos = pos;
        ShiftTime = Time.unscaledTime;
        Version++;
    }

    
    public bool Shift(int step)
    {
        if (NoDriver)
        {
            BlockedTime = Time.unscaledTime;
            return false;
        }
        var np = Math.Clamp(Pos + step, 0, Count - 1);
        if (np == Pos) return false;
        var newDir = Dir(np);
        var lockSpeed = Math.Max(0f, Plugin.ReverseLockSpeed.Value) / 3.6f;
        if (newDir != 0 && newDir * Velocity < -lockSpeed)
        {
            BlockedTime = Time.unscaledTime;
            return false;
        }
        Pos = np;
        LastStep = Math.Sign(step);
        ShiftTime = Time.unscaledTime;
        Version++;
        return true;
    }
}
