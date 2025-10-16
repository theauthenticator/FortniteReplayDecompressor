using Unreal.Core.Contracts;

namespace Unreal.Core.Models
{
    public class PersistentIdProperty : IProperty, IResolvable
    {
        public short? Value { get; set; }
        
        public void Serialize(NetBitReader reader)
        {
            Value = reader.ReadInt16();
        }
        
        public void Resolve(NetGuidCache cache)
        {
            // No resolution needed
        }
        
        public override string ToString()
        {
            return Value?.ToString() ?? "null";
        }
    }
}