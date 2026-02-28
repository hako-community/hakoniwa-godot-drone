using Godot;

namespace hakoniwa.objects.core
{
    public interface IDroneInput
    {
        public Vector2 GetLeftStickInput();
        public Vector2 GetRightStickInput();
        /*
         * A
         */
        public bool IsAButtonPressed();
        public bool IsAButtonReleased();
        /*
         * B
         */
        public bool IsBButtonPressed();
        public bool IsBButtonReleased();
        /*
         * X
         */
        public bool IsXButtonPressed();
        public bool IsXButtonReleased();
        /*
         * Y
         */
        public bool IsYButtonPressed();
        public bool IsYButtonReleased();

        /*
         * Up
         */
        public bool IsUpButtonPressed();
        public bool IsUpButtonReleased();

        /*
         * Down
         */
        public bool IsDownButtonPressed();
        public bool IsDownButtonReleased();

        void DoVibration(bool isRightHand, float frequency, float amplitude, float durationSec);
    }
}

