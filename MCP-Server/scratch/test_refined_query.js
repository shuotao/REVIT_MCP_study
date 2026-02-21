
import { RevitSocketClient } from '../build/socket.js';

async function test() {
    const client = new RevitSocketClient();
    try {
        await client.connect();
        console.log("Connected to Revit");

        // Step 1: Get Active Schema
        console.log("\n--- Step 1: get_active_schema ---");
        const schemaResponse = await client.sendCommand("get_active_schema", {});
        const schema = schemaResponse.data;
        console.log("Categories found:", JSON.stringify(schema, null, 2));

        if (!schema || !schema.Categories || schema.Categories.length === 0) {
            console.log("No categories found in active view.");
            return;
        }

        // Step 2: Get Category Fields
        const cat = schema.Categories.find(c => c.Name === "窗" || c.InternalName === "Windows") || schema.Categories[0];
        const catName = cat.Name;
        console.log(`\n--- Step 2: get_category_fields for ${catName} ---`);
        const fieldsResponse = await client.sendCommand("get_category_fields", { category: catName });
        const fields = fieldsResponse.data;
        console.log(`Fields for ${catName}:`, JSON.stringify(fields, null, 2));

        // Step 3: Get Field Values
        const fieldName = fields.InstanceFields?.find(f => f.includes("窗台高度") || f.includes("Sill Height")) || fields.InstanceFields?.[0];
        if (fieldName) {
            console.log(`\n--- Step 3: get_field_values for ${fieldName} ---`);
            const valuesResponse = await client.sendCommand("get_field_values", { category: catName, fieldName: fieldName });
            console.log(`Values for ${fieldName}:`, JSON.stringify(valuesResponse.data, null, 2));
        }

        // Step 4: Query Elements with Filter
        console.log("\n--- Step 4: query_elements ---");
        const queryParams = {
            category: catName,
            maxCount: 5,
            returnFields: fieldName ? [fieldName, "標記"] : ["標記"]
        };
        const queryResponse = await client.sendCommand("query_elements", queryParams);
        console.log("Query Result:", JSON.stringify(queryResponse.data, null, 2));

    } catch (err) {
        console.error("Test failed:", err.message);
    } finally {
        process.exit();
    }
}

test();
