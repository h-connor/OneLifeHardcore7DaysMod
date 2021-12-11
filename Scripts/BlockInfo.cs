public struct BlockInfo
{
    public int id { get; set; }
    public int x, y, z;

    public BlockInfo(int id, int x, int y, int z)
    {
        this.id = id;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static bool operator ==(BlockInfo a, BlockInfo b)
    {
        return a.id == b.id && a.x == b.x && a.y == b.y && a.z == b.z;
    }

    public static bool operator !=(BlockInfo a, BlockInfo b)
    {
        return a.id != b.id && a.x != b.x && a.y != b.y && a.z != b.z;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return this == (BlockInfo)obj;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public string ToString()
    {
        return id.ToString() + ',' + x.ToString() + ',' + y.ToString() + ',' + z.ToString();
    }
}