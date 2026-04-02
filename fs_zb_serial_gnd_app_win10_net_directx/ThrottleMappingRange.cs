namespace RCCarController
{
    public class ControlMappingRange
    {
        public int InputMin { get; set; }
        public int InputMax { get; set; }
        public int OutputMin { get; set; }
        public int OutputMax { get; set; }

        public ControlMappingRange(int inputMin, int inputMax, int outputMin, int outputMax)
        {
            InputMin = inputMin;
            InputMax = inputMax;
            OutputMin = outputMin;
            OutputMax = outputMax;
        }

        public override string ToString()
        {
            return $"[{InputMin}-{InputMax}] → [{OutputMin}-{OutputMax}]";
        }

        public string ToSettingsString()
        {
            return $"{InputMin},{InputMax},{OutputMin},{OutputMax}";
        }

        public static ControlMappingRange? FromSettingsString(string str)
        {
            var parts = str.Split(',');
            if (parts.Length != 4)
                return null;

            if (int.TryParse(parts[0], out var inMin) &&
                int.TryParse(parts[1], out var inMax) &&
                int.TryParse(parts[2], out var outMin) &&
                int.TryParse(parts[3], out var outMax))
            {
                return new ControlMappingRange(inMin, inMax, outMin, outMax);
            }

            return null;
        }
    }
}
