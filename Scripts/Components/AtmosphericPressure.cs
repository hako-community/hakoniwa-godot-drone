using System;
using Godot;

namespace hakoniwa.objects.core
{
    public class AtmosphericPressure
    {
        // 定数（SI単位系）
        private const double Lb = -0.0065;           // 温度減率 [K/m]
        private const double M = 0.0289644;          // 空気のモル質量 [kg/mol]
        private const double G = 9.80665;            // 重力加速度 [m/s^2]
        private const double R = 8.31432;            // 気体定数 [J/(mol·K)]

        private const double DefaultSeaLevelPressure = 101325.0; // Pa

        // 気温（℃）→ ケルビン（K）
        public static double CelsiusToKelvin(double celsius) => celsius + 273.15;

        // atm → Pa
        public static double AtmToPascal(double atm) => atm * 101325.0;

        // Pa → atm
        public static double PascalToAtm(double pascal) => pascal / 101325.0;

        // 気圧（Pa）を高度（m）から求める
        public static double ConvertAltToBaro(double alt, double sea_level_atm, double sea_level_temp)
        {
            double Tb = CelsiusToKelvin(sea_level_temp);

            if (alt <= 11000.0)
            {
                double Pb = AtmToPascal(sea_level_atm);
                if (Pb <= 0.1)
                {
                    Pb = DefaultSeaLevelPressure;
                }

                double temp = Tb + (Lb * alt);
                if (temp <= 0.0) return 0.0;

                return Pb * Math.Pow(Tb / temp, (G * M) / (R * Lb));
            }
            else if (alt <= 20000.0)
            {
                double temp_f = 11000.0;
                double a = ConvertAltToBaro(temp_f, sea_level_atm, sea_level_temp);
                double c = Tb + (alt * Lb);
                return a * Math.Exp((-G * M * (alt - temp_f)) / (R * c));
            }

            return 0.0;
        }
    }
}
