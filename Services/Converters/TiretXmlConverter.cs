using System.Xml.Linq;
using ModelDto.EditorialUnits;

namespace WordParserCore.Services.Converters
{
    /// <summary>
    /// Konwerter dla tiretów - transformuje TiretDto do XML.
    /// </summary>
    // public class TiretXmlConverter
    // {
    //     public XElement ToXml(TiretDto tiret, bool generateGuids = false)
    //     {
    //         var newElement = new XElement(XmlConstants.Tiret,
    //             new XAttribute("id", tiret.Id));
    //         if (generateGuids) newElement.Add(new XAttribute("guid", tiret.Guid));
    //         newElement.AddFirst(new XElement(XmlConstants.Number, tiret.Number?.Value ?? string.Empty));
    //         newElement.Add(new XElement("text", tiret.ContentText));

    //         // TODO: Dodać Amendment conversion
    //         foreach (var amendment in tiret.Amendments)
    //         {
    //             // newElement.Add(amendment.ToXML(generateGuids));
    //         }

    //         return newElement;
    //     }
    // }
}
