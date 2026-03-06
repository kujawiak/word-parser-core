using System.Xml.Linq;
using ModelDto.EditorialUnits;

namespace WordParserCore.Services.Converters
{
    /// <summary>
    /// Konwerter dla artykułów - transformuje ArticleDto do XML.
    /// </summary>
    public class ArticleXmlConverter
    {
        // public XElement ToXml(Article article, bool generateGuids = false)
        // {
        //     var newElement = new XElement(XmlConstants.Article,
        //         new XAttribute("id", article.Id));
        //     if (generateGuids) newElement.Add(new XAttribute("guid", article.Guid));
        //     newElement.AddFirst(new XElement(XmlConstants.Number, article.Number));

        //     if (article.IsAmending)
        //     {
        //         foreach (var journal in article.Journals)
        //         {
        //             newElement.Add(new XElement("publication",
        //                 new XAttribute("year", journal.Year),
        //                 new XAttribute("positions", string.Join(",", journal.Positions))));
        //         }
        //     }

        //     foreach (var paragraph in article.Paragraphs)
        //     {
        //         var paragraphConverter = new ParagraphXmlConverter();
        //         newElement.Add(paragraphConverter.ToXml(paragraph, generateGuids));
        //     }

        //     return newElement;
        // }
    }
}
