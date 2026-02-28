using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using hakoniwa.environment.interfaces;
using hakoniwa.pdu.interfaces;

namespace hakoniwa.sim.core
{

    public interface IHakoAsset
    {
        bool Initialize(List<IHakoObject> hakoObectList);
        Task<bool> RegisterOnHakoniwa();
        Task<bool> UnRegisterOnHakoniwa();
        bool Execute();
        IPduManager GetPduManager();
        IHakoCommunicationService GetHakoCommunicationService();
    }
}
