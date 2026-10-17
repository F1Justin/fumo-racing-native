using System;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeCustomRaceRules
    {
        public int itemRespawn;
        public int topSpeed = 100, acceleration = 100, friction = 100, steering = 100, gravity = 100, jump = 100;
        public static readonly string[] Rows = { "Continue", "Laps", "count", "type", "respawn", "TopSpeed", "Accel", "Friction", "steering", "gravity", "jump" };
        public static int Minimum(int row) => row == 1 || row == 2 ? 1 : row == 5 || row == 6 || row == 9 ? 20 : row == 8 ? 10 : 0;
        public static int Maximum(int row) => row == 1 || row == 2 ? 100 : row == 3 ? 2 : row == 4 ? 1 : row == 10 ? 400 : 300;
        public int Value(int row) => row == 4 ? itemRespawn : row == 5 ? topSpeed : row == 6 ? acceleration : row == 7 ? friction : row == 8 ? steering : row == 9 ? gravity : row == 10 ? jump : 0;
        public void Set(int row, int value)
        {
            value = Math.Max(Minimum(row), Math.Min(Maximum(row), value));
            switch (row) { case 4: itemRespawn = value; break; case 5: topSpeed = value; break; case 6: acceleration = value; break; case 7: friction = value; break; case 8: steering = value; break; case 9: gravity = value; break; case 10: jump = value; break; }
        }
        public NativeCustomRaceRules Copy() => (NativeCustomRaceRules)MemberwiseClone();
        public void Validate()
        {
            for (int row = 4; row < Rows.Length; row++)
                if (Value(row) < Minimum(row) || Value(row) > Maximum(row)) throw new ArgumentOutOfRangeException(Rows[row]);
        }
        public NativeKartSettings Apply(NativeKartSettings original)
        {
            var settings = original.Copy();
            float speed = topSpeed / 100f, accel = acceleration / 100f, grip = friction / 100f, turn = steering / 100f;
            settings._topSpeed *= speed; settings._topSpeedGrass *= speed; settings._topSpeedCap *= speed;
            // Original IL2CPP 0x30DA50 multiplies acceleration by the same ratio twice.
            settings._acceleration *= accel * accel;
            settings._friction *= grip; settings._frictionGrass *= grip;
            settings._turnMax *= turn; settings._turnForce *= turn; settings._turnFriction *= turn;
            settings._gravity *= gravity / 100f; settings._jumpForce *= jump / 100f;
            return settings;
        }
    }
}
