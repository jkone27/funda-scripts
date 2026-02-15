# F# Funda

An F# amsterdam/NL based property search tool written in F# that visualizes listings on interactive maps using the Funda mobile API. Experimental, made this just for fun at the moment.

## Overview

This project searches property listings from [Funda.nl](https://funda.nl) using reverse-engineered API endpoints and visualizes results on interactive maps with [Plotly.NET](https://plotly.net).

**Inspired by:** [0xMH/pyfunda](https://github.com/0xMH/pyfunda) Python implementation

## Features

- Search Funda.nl listings by location
- Fetch detailed property information
- Visualize listings on interactive maps
- Built with F# and Plotly.NET

## Getting Started

The main script is in `funda.fsx` - requires:
- F# 
- Dependencies:
  - [FSharp.Data](https://fsprojects.github.io/FSharp.Data/) - JsonProvider for type-safe JSON parsing
  - [FsHttp](https://github.com/fsprojects/FsHttp) - Functional HTTP client
  - [Plotly.NET](https://plotly.net/) - Interactive visualization

## References

- [Plotly.NET Mapbox Documentation](https://plotly.net/geo-map-charts/geo-vs-mapbox.html)
- [Plotly Scatter Plot Maps](https://plotly.com/python/scatter-plots-on-maps/)
- [Plotly F# Examples](https://plotly.com/fsharp/mapbox-county-choropleth/)
