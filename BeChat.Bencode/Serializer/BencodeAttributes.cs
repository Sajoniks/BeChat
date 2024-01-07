namespace BeChat.Bencode.Serializer;

public class BencodePropertyNameAttribute : Attribute
{
    private string _propName;
    
    public BencodePropertyNameAttribute(string propName)
    {
        _propName = propName;
    }

    public string PropName => _propName;
}