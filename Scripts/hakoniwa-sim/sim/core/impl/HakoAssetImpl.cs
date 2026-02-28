using System.Collections;
using static hakoniwa.sim.core.impl.HakoCppWrapper;
using System;
using System.Collections.Generic;
using hakoniwa.environment.interfaces;
using hakoniwa.environment.impl;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.core;
using System.Threading.Tasks;
//#if UNITY_EDITOR
using Godot;
//#endif

namespace hakoniwa.sim.core.impl
{
    public class HakoAssetImpl : IHakoAsset, IHakoCommand
    {
        private string my_asset_name;
        private string pduConfigPath;
        private string customJsonFilePath;
        private long asset_time_usec = 0;
        private long delta_time_usec;
        private bool isReady = false;
        private List<IHakoObject> hakoObectList;
        private IPduManager pduManager;
        private IEnvironmentService hako_service;
        private IHakoCommunicationService hako_pdu;


        public HakoAssetImpl(string asset_name, long d_time_usec, string pdu_config_path, string custom_json_file_path)
        {
            my_asset_name = asset_name;
            delta_time_usec = d_time_usec;
            pduConfigPath = pdu_config_path;
            customJsonFilePath = custom_json_file_path;
        }
        public bool Initialize(List<IHakoObject> list)
        {
            hakoObectList = list;
            isReady = HakoCppWrapper.asset_init();
            if (isReady)
            {
                hako_service = HakoEnvironmentServiceFactory.Create(my_asset_name);
                if (hako_service.GetCommunication() is IHakoCommunicationService)
                {
                    hako_pdu = (IHakoCommunicationService)hako_service.GetCommunication();
                }
                else
                {
                    throw new Exception("Can not cast IHakoCommunicationService");
                }
                pduManager = new PduManager(hako_service, pduConfigPath, customJsonFilePath);
            }
            return isReady;
        }

        private void PollEvent()
        {
            HakoSimAssetEvent ev = HakoCppWrapper.asset_get_event(my_asset_name);
            switch (ev)
            {
                case HakoSimAssetEvent.HakoSimAssetEvent_Start:
                    StartCallback();
                    break;
                case HakoSimAssetEvent.HakoSimAssetEvent_Stop:
                    StopCallback();
                    break;
                case HakoSimAssetEvent.HakoSimAssetEvent_Reset:
                    ResetCallback();
                    break;
                default:
                    break;
            }
        }
        private void StartCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventStart();
            }
            HakoCppWrapper.asset_start_feedback(my_asset_name, true);
        }
        private void StopCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventStop();
            }
            HakoCppWrapper.asset_stop_feedback(my_asset_name, true);
        }
        private void ResetCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventReset();
            }
            HakoCppWrapper.asset_reset_feedback(my_asset_name, true);
        }
        public bool Execute()
        {
            //for heartbeat 
            long w_time = HakoCppWrapper.get_wrold_time();
            HakoCppWrapper.asset_notify_simtime(my_asset_name, w_time);
            this.PollEvent();

            if (this.GetState() != SimulationState.Running)
            {
                return false;
            }

            /********************
             * Hakoniwa Time Sync
             ********************/
            if (HakoCppWrapper.asset_is_pdu_created() == false)
            {
                /* nothing to do */
                return false;
            }
            else if (HakoCppWrapper.asset_is_simulation_mode())
            {
                long world_time = this.GetWorldTime();
                long next_asset_time_usec = this.asset_time_usec + this.delta_time_usec;
                if (next_asset_time_usec <= world_time)
                {
                    this.asset_time_usec = next_asset_time_usec;
                    HakoCppWrapper.asset_notify_simtime(my_asset_name, this.asset_time_usec);
                }
                else
                {
                    // can not do simulation because world time is slow...
                    //GD.Print($"Can not do simulation because world time is slow... asset_time={asset_time_usec} world_time={world_time}");
                    return false;
                }
                hako_pdu.EventTick();
                foreach (var obj in hakoObectList)
                {
                    obj.EventTick();
                }
                return true;
            }
            else if (HakoCppWrapper.asset_is_pdu_sync_mode(my_asset_name))
            {
                //GD.Print("Can not do simulation because sync mode...");
                HakoCppWrapper.asset_notify_write_pdu_done(my_asset_name);
                return false;
            }
            //GD.Print("Can not do simulation why??");
            return false;
        }

        public SimulationState GetState()
        {
            if (!isReady)
            {
                return SimulationState.Terminated;
            }
            HakoState state = HakoCppWrapper.simevent_get_state();
            switch (state)
            {
                case HakoState.Stopped:
                case HakoState.Resetting:
                    return SimulationState.Stopped;
                case HakoState.Runnable:
                    return SimulationState.Runnable;
                case HakoState.Running:
                    return SimulationState.Running;
                case HakoState.Stopping:
                    return SimulationState.Stopping;
                case HakoState.Error:
                case HakoState.Terminated:
                default:
                    return SimulationState.Terminated;
            }
        }


        public async Task<bool> RegisterOnHakoniwa()
        {
            if (isReady)
            {
                var ret = await pduManager.StartService();
                if (ret)
                {
                    isReady = HakoCppWrapper.asset_register_polling(my_asset_name);
//#if UNITY_EDITOR
                    GD.Print($"asset register result({isReady}): {my_asset_name}");
//#endif
                }
                else
                {
                    isReady = false;
//#if UNITY_EDITOR
                    GD.PrintErr($"pduManager StartService error: {my_asset_name}");
//#endif
                }
            }
            return isReady;
        }

        public Task<bool> UnRegisterOnHakoniwa()
        {
            if (isReady)
            {
                var ret = pduManager.StopService();
                if (ret == false)
                {
                    //TODO ERROR LOG
                }
                return Task.FromResult<bool>(HakoCppWrapper.asset_unregister(my_asset_name));
            }
            return Task.FromResult<bool>(false);
        }
        public long GetWorldTime()
        {
            if (isReady)
            {
                return HakoCppWrapper.get_wrold_time();
            }
            return 0;
        }

        public bool SimulationStart()
        {
            if (isReady)
            {
                return HakoCppWrapper.simevent_start();
            }
            return false;
        }

        public bool SimulationStop()
        {
            if (isReady)
            {
                return HakoCppWrapper.simevent_stop();
            }
            return false;
        }

        public bool SimulationReset()
        {
            if (isReady)
            {
                asset_time_usec = 0;
                return HakoCppWrapper.simevent_reset();
            }
            return false;
        }

        public IPduManager GetPduManager()
        {
            if (isReady)
            {
                return pduManager;
            }
            return null;
        }

        public IHakoCommunicationService GetHakoCommunicationService()
        {
            if (isReady)
            {
                return hako_pdu;
            }
            return null;
        }
    }
}
