/**
 * Exterior Wall Opening Check Script
 * Tests Article 45 and Article 110 compliance
 */

import { RevitSocketClient } from '../build/socket.js';

import path from 'path';

async function testExteriorWallOpeningCheck() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        console.log('Connecting to Revit MCP Server...');
        await client.connect();
        console.log('Connected!\n');

        console.log('Running CheckExteriorWallOpenings...');
        const reportFile = path.join(process.cwd(), 'exterior_wall_check_report.json');
        const result = await client.sendCommand('check_exterior_wall_openings', {
            checkArticle41: true,
            checkArticle42: true,
            checkArticle45: true,
            checkArticle110: false,
            siteBoundary: [
                { x: 0, y: 0 },
                { x: 50000, y: 0 },
                { x: 50000, y: 50000 },
                { x: 0, y: 50000 }
            ], // Default site boundary (50m x 50m)
            colorizeViolations: true,
            createDimensions: true,
            exportReport: true,
            reportPath: reportFile
        });

        if (result.success) {
            console.log('\nCheck Completed!\n');
            console.log('Summary:');
            console.log(`  - Total Walls: ${result.data.summary.totalWalls}`);
            console.log(`  - Total Openings: ${result.data.summary.totalOpenings}`);
            console.log(`  - Violations: ${result.data.summary.violations}`);
            console.log(`  - Warnings: ${result.data.summary.warnings}`);
            console.log(`  - Passed: ${result.data.summary.passed}`);
            console.log(`  - Property Lines: ${result.data.summary.propertyLineCount}`);

            // Show details
            if (result.data.details && result.data.details.length > 0) {
                console.log('\nDetails (First 3):');
                result.data.details.slice(0, 3).forEach((detail, index) => {
                    console.log(`\n  [${index + 1}] Opening ID: ${detail.openingId}`);
                    console.log(`      Type: ${detail.openingType}`);
                    console.log(`      Location: (${detail.location.x}, ${detail.location.y}, ${detail.location.z}) mm`);
                    console.log(`      Area: ${detail.area} m²`);

                    if (detail.article45) {
                        // C# classes are serialized as PascalCase by default
                        console.log(`      Article 45: ${detail.article45.OverallStatus}`);
                        const dist = detail.article45.DistanceToBoundary;
                        console.log(`        - Distance to Boundary: ${typeof dist === 'number' ? dist.toFixed(2) : 'N/A'} m`);
                        console.log(`        - ${detail.article45.BoundaryMessage}`);
                    }

                    if (detail.article110) {
                        console.log(`      Article 110: ${detail.article110.OverallStatus}`);
                        if (detail.article110.RequiredFireRating > 0) {
                            console.log(`        - Required Fire Rating: ${detail.article110.RequiredFireRating} hr`);
                        }
                        console.log(`        - ${detail.article110.BoundaryFireMessage}`);
                    }
                });

                if (result.data.details.length > 3) {
                    console.log(`\n  ... and ${result.data.details.length - 3} more. See report for details.`);
                }
            }

            console.log(`\n${result.data.message}`);
            console.log('\nVisualization:');
            console.log('  - Revit elements have been colored (Red/Orange/Green)');
            console.log('  - Full report exported to: D:\\Reports\\exterior_wall_check.json');

        } else {
            console.error('Check Failed:', result.error);
        }

    } catch (error) {
        console.error('Error:', error.message);
        console.error('\nTroubleshooting:');
        console.error('  1. Is Revit running?');
        console.error('  2. Is RevitMCP Add-in loaded?');
        console.error('  3. Is MCP Server running on Port 8966?');
        console.error('  4. Are Property Lines defined in the project?');
    } finally {
        client.disconnect();
        console.log('\nDisconnected.');
    }
}

testExteriorWallOpeningCheck().catch(console.error);
