namespace LostRegalia.Sim
{
    /// <summary>스냅숏을 비교하기 위한 해시(FNV-1a 64비트).</summary>
    public static class Hash
    {
        public static ulong Of(byte[] bytes)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
