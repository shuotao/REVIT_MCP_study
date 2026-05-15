import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
ws.on('open', () => {
    ws.once('message', data => {
        console.log(data.toString());
        process.exit(0);
    });
    ws.send(JSON.stringify({
        CommandName: 'execute_csharp', 
        Parameters: { 
            Code: `
                var ids = new[] { 14322002, 14291564, 14291787 };
                var result = new System.Collections.Generic.List<string>();
                foreach(var idStr in ids) {
                    var elem = doc.GetElement(new ElementId(idStr));
                    if (elem != null) {
                        string name = elem.Name;
                        string family = (elem as FamilyInstance)?.Symbol?.FamilyName ?? "N/A";
                        string category = elem.Category?.Name ?? "N/A";
                        string comments = elem.LookupParameter("備註")?.AsString() ?? "N/A";
                        result.Add($"{idStr}: Category={category}, Family={family}, Name={name}, Comments={comments}");
                    }
                }
                return string.Join("\\n", result);
            ` 
        }, 
        RequestId: 'c-' + Date.now()
    }));
});
