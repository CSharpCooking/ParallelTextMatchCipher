using Meisui.Random;
using System.Collections.Generic;

namespace Stego
{
    public class StegoAlg
    {
        private readonly int _containerBitLength;
        private readonly int _hiddenCodeLength;
        private readonly char[,] _etalons;
        private readonly List<int>[] _coordinates;
        private readonly MersenneTwister _generator;

        public StegoAlg(int n, char[,] etalons, char[,] key, MersenneTwister generator)
        {
            _containerBitLength = n * 9 - 12;
            _hiddenCodeLength = (_containerBitLength * 3) / 8;
            _etalons = etalons;
            _coordinates = GetCoordinates(key);
            _generator = generator;
        }

        public StegoAlg(int n, char[,] etalons, char[,] key)
            : this(n, etalons, key, new MersenneTwister())
        { }

        public int HiddenCodeLength => _hiddenCodeLength;

        public byte[] Hide(byte code)
        {
            var gamma = GenerateGamma();

            for (int i = 0; i < 3; i++)
            {
                byte digit = (byte)(code % 10);
                code /= 10;
                var offset = i * _containerBitLength;

                foreach (var c in _coordinates[digit])
                {
                    var bit = 1 << (7 - (offset + c) % 8);
                    var byteNumber = (offset + c) / 8;
                    gamma[byteNumber] = (byte)(_etalons[digit, c] == '1' ?
                        gamma[byteNumber] | bit : gamma[byteNumber] & ~bit);
                }
            }

            return gamma;
        }

        public bool TryDisclose(byte[] hiddenCode, out byte code)
        {
            code = 0;
            int multiplier = 1;
            for (var i = 0; i < 3; i++)
            {
                var digit = Disclose(hiddenCode, i * _containerBitLength);
                if (digit == -1)
                {
                    return false;
                }
                multiplier *= 10;
                code += (byte)(digit * multiplier);
            }

            return true;
        }

        private int Disclose(byte[] hiddenCode, int offset)
        {
            int byteOffset = offset / 8;
            int bitOffset = 7 - (offset % 8);
            for (var i = 0; i < 10; i++)
            {
                var isMatch = true;
                foreach (var coordinate in _coordinates[i])
                {
                    var @byte = hiddenCode[byteOffset + coordinate / 8];
                    var bitPosition = bitOffset - (coordinate % 8);
                    var bit = @byte & (1 << bitPosition);
                    if ((_etalons[i, coordinate] != '0') != (bit != 0))
                    {
                        isMatch = false;
                        break;
                    }
                }
                if (isMatch)
                {
                    return i;
                }
            }
            return -1;
        }

        private List<int>[] GetCoordinates(char[,] key)
        {
            var coordinates = new List<int>[10];

            for (var i = 0; i < key.GetLength(0); i++)
            {
                coordinates[i] = new List<int>();
                for (var j = 0; j < key.GetLength(1); j++)
                {
                    if (key[i, j] == '1') coordinates[i].Add(j);
                }
            }

            return coordinates;
        }

        private byte[] GenerateGamma()
        {
            var gamma = new byte[_hiddenCodeLength];

            for (int i = 0; i < gamma.Length; i++)
            {
                gamma[i] = (byte)_generator.genrand_Int32();
            }

            return gamma;
        }
    }
}
