#r "nuget: FSharp.Data"
#r "nuget: FsHttp.FSharpData"
#r "nuget: Plotly.NET"

open FsHttp
open FsHttp.FSharpData
open FSharp.Data
open System
open System.Text.Json
open Plotly.NET
open Plotly.NET.LayoutObjects
open System

[<AutoOpen>]
module Funda = 

  // Define JSON providers based on actual pyfunda API responses
  type SearchResponse = JsonProvider<"./fundaSearchResp.json">

  type DetailResponse = JsonProvider<"./fundaDetailResp.json">

  // TODO: from website not from api
  type SearchRequest = JsonProvider<"./fundaSearchReq.json", SampleIsList=true>

  // Base URLs for Funda mobile API (reverse engineered from Dart/Flutter app)
  let apiBase = "https://listing-detail-page.funda.io/api/v4/listing/object/nl"
  let apiSearchBase = "https://listing-search-wonen.funda.io/_msearch/template"

  // Generate random traceparent header as per Funda app
  let generateTraceparent () =
    let traceId = Random().Next(100000000, 999999999).ToString("X16")
    let parentId = Random().Next(100000, 999999).ToString("X8")
    $"00-{traceId}-{parentId}-00"

  // Generate Datadog parent-id for tracing
  let generateDatadogParentId () =
    Random().Next(1000000, 9999999).ToString("X")

  // Generate Datadog trace state
  let generateTracestate (parentId: string) =
    $"dd=s:0;o:rum;p:{parentId}"

  type OfferingType = Buy | Rent

  // Function to search listings using Elasticsearch Multi Search Template API
  let searchListings (location:string) (page:int) (offering: OfferingType)=
    // Build NDJSON payload using triple-quoted strings for clarity
    // First line: index specification
    let indexLine = """{"index":"listings-wonen-searcher-alias-prod"}"""
    
    // Second line: query template with parameters - build using sprintf
    let priceKey = 
      match offering with
      | OfferingType.Buy -> "selling_price"
      | OfferingType.Rent -> "renting_price"

    let jsonReplacer = sprintf """{
        "id":"search_result_20250805",
        "params":{ 
          "price": { "%s": { "from": 0, "to": 300000 }},
          "selected_area":["%s"],
          "offering_type":"%s",
          "type":["single"],
          "zoning":["residential"],
          "availability":["available"], 
          "page":{"from":%d}
        }
      }"""

    let queryLine = 
      jsonReplacer 
        priceKey 
        location 
        (offering.ToString().ToLower()) 
        (page * 15)
      |> SearchRequest.Parse 
      |> _.JsonValue.ToString(JsonSaveOptions.DisableFormatting)
    
    printfn $"QUERY: {queryLine}"

    // Combine into NDJSON format (newline-delimited JSON)
    let payload = indexLine + "\n" + queryLine + "\n"
    
    let parentId = generateDatadogParentId ()
    let traceparent = generateTraceparent ()
    let tracestate = generateTracestate parentId
    
    printfn "[DEBUG] Sending search request for: %s (page %d)" location page
    printfn "[DEBUG] Payload (first 200 chars): %s" (payload.Substring(0, min 200 payload.Length))
    
    let response = 
      http {
        POST apiSearchBase
        UserAgent "Dart/3.9 (dart:io)"
        Referer "https://www.funda.nl/"
        Accept "application/json"
        header "accept-encoding" "gzip"
        header "x-datadog-sampling-priority" "0"
        header "x-datadog-origin" "rum"
        header "tracestate" tracestate
        header "x-datadog-parent-id" parentId
        header "traceparent" traceparent
        body
        json payload
      }
      |> Request.send
    
    printfn "[DEBUG] Response status: %A" response.statusCode
    if response.statusCode <> System.Net.HttpStatusCode.OK then
      printfn "[ERROR] Expected 200 OK, got %A" response.statusCode
      try
        let body = response.content.ReadAsStringAsync().Result
        printfn "[ERROR] Response body:\n%s" body
      with
      | ex -> printfn "[ERROR] Could not read body: %s" ex.Message
    
    let jResp =
      response
      |> Response.toJson
    
    // only run ONCE the first time, and if any api update
    // System.IO.File.WriteAllText("./fundaSearchResp.json", jResp.ToString())
    
    jResp|> SearchResponse.Load

  // Function to get detailed listing info by ID (tinyId or globalId)
  let getListingDetail (id:int) =
    let detailJson = 
      http {
        GET $"{apiBase}/{id}"
        UserAgent "Dart/3.9 (dart:io)"
        header "x-funda-app-platform" "android"
        header "accept-encoding" "gzip"
        header "x-datadog-sampling-priority" "0"
        header "x-datadog-origin" "rum"
        header "traceparent" (generateTraceparent ())
      }
      |> Request.send
      |> Response.toJson

    // only run ONCE the first time, and if any api update
    // System.IO.File.WriteAllText("./fundaDetailResp.json", detailJson.ToString())

    detailJson
    |> DetailResponse.Load




// APP..

printfn "[START] Running Funda script...\n"
let results = searchListings "amsterdam"  0 OfferingType.Buy
let firstResponse = results.Responses.[0]
let hits = firstResponse.Hits.Hits
printfn "[SUCCESS] Found %d results on page 0\n" hits.Length

// DEBUG log JSON response
// printfn $"JSON: {results.JsonValue}"

if hits.Length = 0 then
  printfn "[INFO] No listings returned, check API response"
else
  // Fetch coordinates for each listing
  let listings = 
    hits 
    |> Array.toList
    |> List.map (fun hit ->
      try
        let detail = getListingDetail hit.Id
        let coords = detail.Coordinates
        let price = hit.Source.Price.SellingPrice[0]
        let title = sprintf "%s, %s" detail.AddressDetails.Title detail.AddressDetails.City
        (coords.Latitude, coords.Longitude, title, price)
      with ex ->
        printfn "[WARN] Failed to get details for listing %d: %s" hit.Id ex.Message
        (0.0m, 0.0m, "Unknown", 0)
    )
  
  // Create map visualization if we have coordinates
  if listings.Length > 0 then
    printfn "\n[MAP] Creating visualization with %d listings..." listings.Length
    
    let coords = listings |> List.map (fun (lat, lon, _, _) -> (lon, lat)) |> List.toSeq
    let titles = listings |> List.map (fun (_, _, title, price) -> sprintf "%s (€%d)" title price)
    let prices = listings |> List.map (fun (_, _, _, price) -> price) |> Array.ofList
    
    // Load Amsterdam GeoJSON from open data source
    printfn "[MAP] Loading Amsterdam district GeoJSON..."
    let geoJsonUrl = "https://raw.githubusercontent.com/codeforgermany/click_that_hood/master/public/data/amsterdam.geojson"
    
    let geoJson = 
      Http.RequestString geoJsonUrl |> JsonValue.Parse

    let mapbox = 
      Mapbox.init(
        Style = StyleParam.MapboxStyle.CartoPositron,
        Center = (4.89, 52.37),
        Zoom = 11.0
      )

    // Create base choropleth map with neighborhoods
    let map = 
        printfn "[MAP] Rendering choropleth with neighborhood boundaries..."
        // Simple neighborhood coloring by number of listings (as proxy for activity)
        let neighborhoodIndexes = Array.init (listings.Length) (fun i -> i.ToString())
        
        let choropleth =
          Chart.ChoroplethMapbox(
            locations = neighborhoodIndexes,
            z = prices,
            geoJson = geoJson,
            FeatureIdKey = "id"
          )
        
        // Add scatter points overlay for individual listings
        let scatter =
          Chart.PointMapbox(coords, MultiText = titles, UseDefaults = true)
          |> Chart.withMarkerStyle(Size = 10, Color = Color.fromString "blue", Opacity = 0.8)
        
        // Combine both traces
        [choropleth; scatter]
        |> Chart.combine
        |> Chart.withMapbox mapbox
        |> Chart.withColorBarStyle("Price (€)")
        |> Chart.withTitle "Amsterdam Funda Listings by District"
        |> Chart.withSize(1000, 800)

    // Display map
    printfn "[MAP] Displaying map with %d listings..." listings.Length
    map
    |> Chart.show
