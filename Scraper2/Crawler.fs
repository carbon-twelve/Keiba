open HtmlAgilityPack
open System
open System.IO
open System.Net
open System.Linq
open System.Text
open System.Text.RegularExpressions
open System.Net.Http
open NodaTime
open NodaTime.Text
open System.Diagnostics
open System.Threading

let undefined() = System.NotImplementedException() |> raise

let log = TraceSource("Log")
let rootDir: string = @"C:\Users\Takayuki\Projects\Keiba"
let baseUrl: string = "http://db.netkeiba.com"

let sleep () = Thread.Sleep(15000)
let client = new HttpClient()
let crawlSemaphore = new Semaphore(1, 1)

let rec retry (count: int) (async: Async<'T>): 'T =
    if count > 3 then failwith "Exausted retries" else
    let result =
        try Some(Async.RunSynchronously async)
        with _ -> None
    match result with
        | Some(x) -> x
        | None -> Thread.Sleep(600 * 1000); retry (count + 1) async

let loadHtmlAsStringAsync (uri: Uri): Async<string> =
    async {
        crawlSemaphore.WaitOne() |> ignore
        log.TraceInformation("Downloading {0}", uri); log.Flush()
        let! stream = client.GetStreamAsync(uri) |> Async.AwaitTask
        use reader = new StreamReader(stream, Encoding.GetEncoding("EUC-JP"))
        let content = reader.ReadToEnd()
        log.TraceInformation("Completed downloading {0}", uri); log.Flush()
        sleep ()
        crawlSemaphore.Release() |> ignore
        return content
    }

let loadHtmlAsync (uri: Uri): Async<HtmlDocument> =
    async {
        let! content = loadHtmlAsStringAsync(uri)
        let document = HtmlDocument()
        document.LoadHtml(content)
        return document
    }

let dateRangeMonthly (startDate: LocalDate) (endDate: LocalDate): LocalDate seq =
    Seq.unfold
        (fun currentDate -> if currentDate > endDate then None else Some(currentDate, currentDate.PlusMonths(1)))
        startDate

//TODO: Output current state in error
let monthlyPages (startDate: LocalDate) (endDate: LocalDate): HtmlDocument seq =
    seq {
        for currentMonth in dateRangeMonthly startDate endDate do
            let currentMonthPath = "/?pid=race_top&date=" + currentMonth.ToString("yyyyMMdd", null)
            yield loadHtmlAsync (new Uri(baseUrl + currentMonthPath)) |> retry 0
    }

let raceListRegex = Regex("/race/list/(\d+)")
let localDatePattern = LocalDatePattern.CreateWithInvariantCulture("yyyyMMdd")

let raceListPages (startDate: LocalDate) (endDate: LocalDate) (monthlyPage: HtmlDocument): HtmlDocument seq =
    seq {
        for node in monthlyPage.DocumentNode.SelectNodes("//table[@summary=\"レーススケジュールカレンダー\"]//a") do
            let raceListPath = node.Attributes.["href"].Value
            if raceListRegex.IsMatch(raceListPath) then
                let raceDate =
                    let result = localDatePattern.Parse(raceListRegex.Match(raceListPath).Groups.[1].Value)
                    result.GetValueOrThrow()
                if startDate <= raceDate && raceDate <= endDate then
                    yield loadHtmlAsync (new Uri(baseUrl + raceListPath)) |> retry 0
    }

let raceNumberRegex = Regex("/race/(\d+)/")

let racePages (raceListPage: HtmlDocument): (string * string) seq =
    seq {
        for node in raceListPage.DocumentNode.SelectNodes("//dl[@class=\"race_top_data_info fc\"]/dd/a") do
            let racePath = node.Attributes.["href"].Value
            if raceNumberRegex.IsMatch(racePath) then
                let raceNumber = raceNumberRegex.Match(racePath).Groups.[1].Value
                let html = loadHtmlAsStringAsync (new Uri(baseUrl + racePath)) |> retry 0
                yield (html, raceNumber)
    }

let saveRacePageAsync (racePage: string, raceNumber: string): Async<unit> =
    async {
        let filePath = rootDir + @"\race\" + raceNumber + ".html"
        use writer = new StreamWriter(File.Create(filePath), Encoding.GetEncoding("EUC-JP"))
        log.TraceInformation("Writing to {0}", filePath); log.Flush()
        writer.Write(racePage)
        writer.Flush()
        log.TraceInformation("Completed writing to {0}", filePath); log.Flush();
    }

let crawl (startDate: LocalDate) (endDate: LocalDate): unit =
    for monthlyPage in monthlyPages startDate endDate do
        for raceListPage in raceListPages startDate endDate monthlyPage do
            for racePage in racePages raceListPage do
                Async.Start (saveRacePageAsync racePage)
            
[<EntryPoint>]
let main argv = 
    use consoleListener = new ConsoleTraceListener()
    log.Listeners.Add(consoleListener) |> ignore
    log.Switch.Level <- SourceLevels.Verbose
    crawl (LocalDate(1987,11,1)) (LocalDate(2016,2,28)) |> ignore
    0 // return an integer exit code