using SharpDX.DirectInput;
using System;

namespace RCCarController
{
    public class GameControllerManager
    {
        private DirectInput? directInput;
        private Joystick? joystick;
        private IList<DeviceInstance>? gameControllers;

        public event Action<int, int>? ControlValuesChanged; // steering, throttle

        public void Initialize()
        {
            try
            {
                directInput = new DirectInput();
                gameControllers = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices);
                if (gameControllers.Count == 0)
                {
                    gameControllers = directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices);
                }

                if (gameControllers.Count > 0)
                {
                    joystick = new Joystick(directInput, gameControllers[0].InstanceGuid);
                    joystick.Properties.BufferSize = 128;
                    joystick.Acquire();
                }
            }
            catch (Exception)
            {
                // Handle error
            }
        }

        public void Poll()
        {
            if (joystick == null) return;

            try
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();

                int steeringAxis = state.X;
                int steeringValue = (int)MapRange(steeringAxis, 0, 65535, 0, 180);

                int throttleAxis = state.Y;
                int throttleValue = (int)MapRange(throttleAxis, 0, 65535, 0, 180);

                ControlValuesChanged?.Invoke(steeringValue, throttleValue);
            }
            catch (Exception)
            {
                // Handle error
            }
        }

        private double MapRange(double value, double fromMin, double fromMax, double toMin, double toMax)
        {
            return (value - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
        }

        public void Dispose()
        {
            if (joystick != null)
            {
                joystick.Unacquire();
                joystick.Dispose();
            }
            directInput?.Dispose();
        }
    }
}
