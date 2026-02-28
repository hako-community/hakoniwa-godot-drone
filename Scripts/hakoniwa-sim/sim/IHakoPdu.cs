using System.Collections;
using System.Collections.Generic;
using hakoniwa.pdu.interfaces;
using   Godot;

namespace hakoniwa.sim
{
    public interface IHakoPdu
    {
        IPduManager GetPduManager();
        bool DeclarePduForWrite(string robotName, string pduName);
        bool DeclarePduForRead(string robotName, string pduName);
    }
}
