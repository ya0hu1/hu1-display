using System;
using System.IO;
using System.Xml;
using System.Globalization;

namespace DeskMateLearning
{
    // Export a *copy* for offline driver maintenance; never write the installed XML or restart an adapter.
    internal static class VddCapacity
    {
        internal static XmlDocument Draft(Stream source,int count){
            if(count<2 || count>4)throw new ArgumentOutOfRangeException("count");
            var xml=new XmlDocument{PreserveWhitespace=true,XmlResolver=null};
            var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1048576};
            using(var reader=XmlReader.Create(source,settings))xml.Load(reader);
            var nodes=xml.SelectNodes("/vdd_settings/monitors/count");int previous;
            if(nodes.Count!=1 || !Int32.TryParse(nodes[0].InnerText,out previous) || previous<1 || previous>count)
                throw new InvalidDataException(UiText.Get("配置格式不匹配或当前容量更大，未生成副本。"));
            nodes[0].InnerText=count.ToString(CultureInfo.InvariantCulture);return xml;
        }
        internal static string Export(Settings settings,int count){
            string file=Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"VirtualDisplayDriver","vdd_settings.xml");
            XmlDocument xml;using(var input=File.OpenRead(file))xml=Draft(input,count);
            string dir=Path.Combine(settings.StateDirectory,"vdd-drafts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            using(var output=new FileStream(Path.Combine(dir,"vdd_settings.xml"),FileMode.CreateNew,FileAccess.Write))xml.Save(output);
            return dir;
        }
    }
}
