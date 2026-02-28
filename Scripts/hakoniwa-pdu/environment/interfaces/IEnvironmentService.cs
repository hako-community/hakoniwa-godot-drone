using System;
namespace hakoniwa.environment.interfaces
{
    public interface IEnvironmentService
    {
        IFileLoader GetFileLoader();
        ICommunicationService GetCommunication();
        void SetCommunication(ICommunicationService comm);
    }
}
