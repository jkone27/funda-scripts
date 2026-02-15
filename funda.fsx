#r "nuget: FSharp.Data"
#r "nuget: FsHttp.FSharpData"

open FsHttp
open FsHttp.FSharpData
open FSharp.Data
open System
open System.Text.Json

// JSON helper to properly serialize objects to JSON
let serializeJson (obj: obj) =
  JsonSerializer.Serialize(obj)

Fsi.enableDebugLogs()

// Define JSON providers based on actual pyfunda API responses
type SearchResponse = JsonProvider<"""{
  "responses": [
    {
      "hits": {
        "hits": [
          {
            "_id": "7852306",
            "_source": {
              "address": {
                "street_name": "Pieter Calandlaan",
                "house_number": "400",
                "city": "Amsterdam",
                "postal_code": "1012AB",
                "province": "Noord-Holland",
                "neighbourhood": "De Pijp"
              },
              "price": {
                "selling_price": [500000]
              },
              "floor_area": [80],
              "number_of_bedrooms": 2,
              "energy_label": "B",
              "object_type": "apartment",
              "thumbnail_id": ["photo1", "photo2"]
            }
          }
        ]
      }
    }
  ]
}""">

type DetailResponse = JsonProvider<"""{
  "Identifiers": {
    "GlobalId": "7852306",
    "TinyId": "43117443"
  },
  "AddressDetails": {
    "Title": "Reehorst 13",
    "City": "Luttenberg",
    "PostCode": "7261AE",
    "Province": "Gelderland",
    "NeighborhoodName": "Luttenberg",
    "HouseNumber": "13",
    "HouseNumberExtension": ""
  },
  "Price": {
    "BuyPrice": 695000,
    "AskingPrice": 695000,
    "IsAuction": false
  },
  "FastView": {
    "NumberOfBedrooms": 3,
    "LivingArea": 155,
    "PlotArea": 450
  },
  "Media": {
    "Photos": {
      "MediaBaseUrl": "https://cloud.funda.nl/images/{id}",
      "Items": [{"Id": "photo1"}, {"Id": "photo2"}]
    }
  },
  "KenmerkSections": [
    {
      "KenmerkenList": [
        {"Label": "Woonoppervlakte", "Value": "155 m²"},
        {"Label": "Perceeloppervlakte", "Value": "450 m²"}
      ]
    }
  ]
}""">

// JSON Provider for building search request parameters
type SearchRequestParams = JsonProvider<"""{
  "availability": ["available", "negotiations"],
  "type": ["single"],
  "zoning": ["residential"],
  "selected_area": ["amsterdam"],
  "offering_type": "buy",
  "price": {
    "selling_price": {"from": 200000, "to": 500000}
  },
  "floor_area": {"from": 50, "to": 150},
  "page": {"from": 0}
}""">

// JSON Provider for the Elasticsearch Multi Search Template format
type SearchRequest = JsonProvider<"""{
    "index": "listings-wonen-searcher-alias-prod",
    "id": "search_result_20250805",
    "params": {
        "selected_area": ["amsterdam"],
        "offering_type": "buy",
        "type": ["single"],
        "zoning": ["residential"],
        "page": {"from": 0}
    }
}""">

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


// Function to search listings using Elasticsearch Multi Search Template API
let searchListings (location:string) (page:int) =
  // Build NDJSON payload using triple-quoted strings for clarity
  // First line: index specification
  let indexLine = """{"index":"listings-wonen-searcher-alias-prod"}"""
  
  // Second line: query template with parameters - build using sprintf
  let queryLine = sprintf """{"id":"search_result_20250805","params":{"selected_area":["%s"],"offering_type":"buy","type":["single"],"zoning":["residential"],"page":{"from":%d}}}""" location (page * 15)
  
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
  
  response
  |> Response.toJson
  |> SearchResponse.Load

// Function to get detailed listing info by ID (tinyId or globalId)
let getListingDetail (id:int) =
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
  |> DetailResponse.Load


// Example usage with error handling
try
  printfn "[START] Running Funda script...\n"
  let results = searchListings "amsterdam" 0
  let firstResponse = results.Responses.[0]
  let hits = firstResponse.Hits.Hits
  printfn "[SUCCESS] Found %d results on page 0\n" hits.Length
  
  if hits.Length = 0 then
    printfn "[INFO] No listings returned, check API response"
  else
    for hit in hits do
      let source = hit.Source
      let address = source.Address
      let price = source.Price
      // Safely access array elements
      let priceValue = 
        if price.SellingPrice.Length > 0 then price.SellingPrice.[0] else 0
      
      printfn "Listing ID: %d, Address: %s %d, Price: €%d" hit.Id address.StreetName address.HouseNumber priceValue
      
      // Get detail info for first listing as example (limit API calls)
      if hit.Id = hits.[0].Id && hits.Length > 0 then
        try
          let detail = getListingDetail hit.Id
          let identifiers = detail.Identifiers
          printfn "  Global ID: %d, Tiny ID: %d" identifiers.GlobalId identifiers.TinyId
          printfn "  Bedrooms: %d, Area: %d m²" detail.FastView.NumberOfBedrooms detail.FastView.LivingArea
        with
        | ex -> printfn "  [DETAIL ERROR] Failed to fetch detail: %s" ex.Message
with
| ex -> 
  printfn "[ERROR] Script failed: %s" ex.Message
  printfn "[STACK] %s" ex.StackTrace
