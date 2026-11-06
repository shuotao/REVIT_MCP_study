using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RevitMCP.Core {
 public sealed class BeamElevationLabel {
  public string Number { get; private set; }
  public double? Offset { get; private set; }
  public static BeamElevationLabel Parse(string text) {
   var m=Regex.Match((text??"").Trim(),@"^([A-Za-z]+[0-9]+)\s*(?:\(\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*\)|（\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*）)?$");
   if(!m.Success)throw new ArgumentException("Expected beam number optionally followed by a signed numeric elevation in parentheses.");
   var result=new BeamElevationLabel{Number=m.Groups[1].Value};
   string raw=m.Groups[2].Success?m.Groups[2].Value:m.Groups[3].Value;
   if(raw.Length>0){
    if(!double.TryParse(raw,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double value)||double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Invalid elevation.");
    result.Offset=value;
   }
   return result;
  }
 }
}
