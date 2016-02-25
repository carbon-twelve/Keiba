open HtmlAgilityPack
open System
open System.Net
open System.Linq
open System.Text.RegularExpressions

let crawl () =
    let client = new WebClient()
    let raceTopPage = new HtmlDocument()
    client.Encoding <- System.Text.Encoding.GetEncoding("EUC-JP")
    raceTopPage.LoadHtml(client.DownloadString(new Uri("http://db.netkeiba.com/?pid=race_top")))
    let listUrls = [
        for node in raceTopPage.DocumentNode.SelectNodes("//table[@summary=\"レーススケジュールカレンダー\"]/*/a[@href]") do
            yield node.Attributes.["href"].Value
    ]
    for listUrl in listUrs do
        let regex = new Regex(@"/race/list/(\d+)")
        let fileName = regex.Match(listUrl).Groups.[1] + ".html"
        client.DownloadFile(new Uri("http://db.netkeiba.com" + listUrl), fileName)
    ()

[<EntryPoint>]
let main argv = 
    crawl ()
    0 // return an integer exit code