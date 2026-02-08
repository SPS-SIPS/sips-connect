// utils/generateMccOptions.ts
// eslint-disable-next-line @typescript-eslint/no-require-imports
const fs = require('fs');
// eslint-disable-next-line @typescript-eslint/no-require-imports
const csv = require('csv-parser');
// eslint-disable-next-line @typescript-eslint/no-require-imports
const path = require('path');

interface MCCData {
    mcc: string;
    edited_description: string;
}

const results: MCCData[] = [];

// Create absolute paths to avoid any file location issues
const csvPath = path.join(__dirname, '../public/mcc_codes.csv');
const outputDir = path.join(__dirname, '../data');
const outputPath = path.join(outputDir, 'mccOptions.ts');

console.log(`Reading CSV from: ${csvPath}`);

fs.createReadStream(csvPath)
    .pipe(csv())
    .on('data', (data: any) => {
        results.push({
            mcc: data.mcc,
            edited_description: data.edited_description
        });
    })
    .on('end', () => {
        // Process the data
        const options = results.map((row) => ({
            value: row.mcc.padStart(4, '0'), // Ensure 4-digit format
            label: `${row.mcc.padStart(4, '0')} - ${row.edited_description}`
        }));

        // Sort by MCC code
        options.sort((a, b) => a.value.localeCompare(b.value));

        // Create the output content
        const content = `// Auto-generated from mcc_codes.csv
export interface MCCOption {
  value: string;
  label: string;
}

export const mccOptions: MCCOption[] = ${JSON.stringify(options, null, 2)};`;

        // Ensure directory exists
        if (!fs.existsSync(outputDir)) {
            fs.mkdirSync(outputDir, { recursive: true });
        }

        // Write to file
        fs.writeFileSync(outputPath, content);
        console.log(`Successfully generated ${outputPath}`);
        console.log(`Generated ${options.length} MCC options`);
    })
    .on('error', (err: Error) => {
        console.error('Error processing CSV:', err);
    });