
import { RevitSocketClient } from '../build/socket.js';

async function testSillHeightQuery() {
    const client = new RevitSocketClient();
    
    try {
        console.log("Connecting to Revit...");
        await client.connect();
        console.log("Connected!");

        // Step 1: Read Schema
        console.log("
--- Step 1: Get Active Schema ---");
        const schema = await client.sendCommand("get_active_schema", {});
        console.log("Schema Categories:", JSON.stringify(schema.data.Categories, null, 2));

        // Find the window category
        const windowCat = schema.data.Categories.find(c => c.Name === "窗" || c.Name === "Windows" || c.InternalName === "Windows");
        if (!windowCat) {
            console.error("Window category not found in view.");
            return;
        }
        console.log(`Target Category: ${windowCat.Name} (${windowCat.InternalName})`);

        // Step 2: Get Fields
        console.log("
--- Step 2: Get Category Fields ---");
        const fields = await client.sendCommand("get_category_fields", { category: windowCat.Name });
        console.log("Fields (Instance):", fields.data.InstanceFields);

        // Find Sill Height field (usually "窗台高度" or "Sill Height")
        const sillHeightField = fields.data.InstanceFields.find(f => f.includes("窗台高度") || f.includes("Sill Height"));
        if (!sillHeightField) {
            console.error("Sill Height field not found.");
            return;
        }
        console.log(`Target Field: ${sillHeightField}`);

        // Step 3: Query Elements
        console.log("
--- Step 3: Query Elements (Sill Height < 90) ---");
        const queryResult = await client.sendCommand("query_elements", {
            category: windowCat.Name,
            filters: [
                {
                    field: sillHeightField,
                    operator: "less_than",
                    value: "90"
                }
            ],
            returnFields: [sillHeightField, "高度", "寬度"]
        });

        console.log("Query Results:");
        console.log(JSON.stringify(queryResult.data, null, 2));

    } catch (err) {
        console.error("Error during test:", err);
    } finally {
        client.disconnect();
    }
}

testSillHeightQuery();
