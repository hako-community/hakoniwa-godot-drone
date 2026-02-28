using System.Collections;
using System.Collections.Generic;
using hakoniwa.environment.impl.hako;
using hakoniwa.environment.impl.local;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl
{
    public class HakoEnvironmentServiceFactory
    {
        public static IEnvironmentService Create(string assetName)
        {
            return new HakoEnvironmentService(assetName);
        }

    }
    public class HakoEnvironmentService : IEnvironmentService
    {
        private IFileLoader file_loader;
        private ICommunicationService comm_service;

        protected internal HakoEnvironmentService(string assetName)
        {
            file_loader = new LocalFileLoader();
            comm_service = new HakoCommunicationService(assetName);
        }
        public ICommunicationService GetCommunication()
        {
            return comm_service;
        }

        public IFileLoader GetFileLoader()
        {
            return file_loader;
        }

        public void SetCommunication(ICommunicationService comm)
        {
            this.comm_service = comm;
        }
    }
}
