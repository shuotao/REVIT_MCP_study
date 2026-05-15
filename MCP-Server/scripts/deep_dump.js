import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
ws.on('open', () => {
    ws.once('message', data => {
        const resp = JSON.parse(data.toString());
        console.log(resp.Data);
        process.exit(0);
    });
    ws.send(JSON.stringify({
        CommandName: 'execute_csharp', 
        Parameters: { 
            Code: `
                var res = new System.Collections.Generic.List<string>();
                
                // 1. 抓主模型的套管 14344038
                Element sleeve = doc.GetElement(new ElementId(14344038));
                if (sleeve != null) {
                    res.Add("Sleeve Family: " + (sleeve as FamilyInstance)?.Symbol?.FamilyName);
                    res.Add("Sleeve Type: " + sleeve.Name);
                    foreach(Parameter p in sleeve.Parameters) {
                        if (p.HasValue) {
                            string val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                            res.Add($"[P] {p.Definition.Name} = {val}");
                        }
                    }
                }

                // 2. 抓連結模型的梁 12699317
                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
                foreach(var li in links) {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;
                    Element beam = linkDoc.GetElement(new ElementId(12699317));
                    if (beam != null) {
                        res.Add("--- LINKED BEAM 12699317 ---");
                        res.Add("Beam Family: " + (beam as FamilyInstance)?.Symbol?.FamilyName);
                        res.Add("Beam Type: " + beam.Name);
                        foreach(Parameter p in beam.Parameters) {
                            if (p.HasValue) {
                                string val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                                res.Add($"[P] {p.Definition.Name} = {val}");
                            }
                        }
                        // 也要抓 Type Parameters
                        Element type = linkDoc.GetElement(beam.GetTypeId());
                        if (type != null) {
                            res.Add("--- BEAM TYPE PARAMETERS ---");
                            foreach(Parameter tp in type.Parameters) {
                                if (tp.HasValue) {
                                    string val = tp.StorageType == StorageType.String ? tp.AsString() : tp.AsValueString();
                                    res.Add($"[T] {tp.Definition.Name} = {val}");
                                }
                            }
                        }
                    }
                }
                return string.Join("\\n", res);
            ` 
        }, 
        RequestId: 'dump-' + Date.now()
    }));
});
