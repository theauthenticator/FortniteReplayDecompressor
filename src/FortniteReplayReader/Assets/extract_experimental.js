const fs = require('fs');
const path = require('path');

let ResourceRates = {};

function loadResourceRates(filePath) {
    try {
        console.log('📖 Loading ResourceRates.json...');
        
        if (!fs.existsSync(filePath)) {
            console.log(`❌ Not found at: ${filePath}`);
            return false;
        }

        const json = fs.readFileSync(filePath, 'utf8');
        const data = JSON.parse(json);

        if (!Array.isArray(data) || data.length === 0) {
            console.log('❌ Invalid format');
            return false;
        }

        const root = data[0];
        if (!root.Rows) {
            console.log("❌ No 'Rows' property found");
            return false;
        }

        ResourceRates = {};

        for (const [rowName, rowData] of Object.entries(root.Rows)) {
            if (rowData.Keys && Array.isArray(rowData.Keys) && rowData.Keys.length > 0) {
                const firstKey = rowData.Keys[0];
                if (firstKey.Value !== undefined) {
                    ResourceRates[rowName] = Math.round(firstKey.Value);
                }
            }
        }

        console.log(`✅ Loaded ${Object.keys(ResourceRates).length} resource tiers`);
        console.log('Sample tiers:', Object.keys(ResourceRates).slice(0, 5).join(', '));
        console.log('');
        return true;
    } catch (error) {
        console.log(`❌ Error: ${error.message}`);
        return false;
    }
}

function getAllJsonFiles(dir, fileList = []) {
    const files = fs.readdirSync(dir);

    files.forEach(file => {
        const filePath = path.join(dir, file);
        const stat = fs.statSync(filePath);

        if (stat.isDirectory()) {
            getAllJsonFiles(filePath, fileList);
        } else if (file.endsWith('.json')) {
            fileList.push(filePath);
        }
    });

    return fileList;
}

function extractFromSparseData(sparseData) {
    if (!sparseData) return null;
    
    let resourceTier = null;
    let attributeCategory = null;
    let attributeSubCategory = null;
    
    // Check BuildingResourceAmountOverride
    if (sparseData.BuildingResourceAmountOverride) {
        const override = sparseData.BuildingResourceAmountOverride;
        if (typeof override === 'object' && override.RowName) {
            resourceTier = override.RowName;
        } else if (typeof override === 'string') {
            resourceTier = override;
        }
    }
    
    // Check AttributeInitKeys
    if (sparseData.AttributeInitKeys) {
        const keys = sparseData.AttributeInitKeys;
        attributeCategory = keys.AttributeInitCategory || null;
        attributeSubCategory = keys.AttributeInitSubCategory || null;
    }
    
    return { resourceTier, attributeCategory, attributeSubCategory };
}

function extractHarvestData(rootPath) {
    console.log('🎯 EXTRACTING HARVESTABLES...\n');
    
    const jsonFiles = getAllJsonFiles(rootPath);
    console.log(`📄 Scanning ${jsonFiles.length} files...\n`);
    
    const harvestables = [];
    let processed = 0;
    let foundCount = 0;

    for (const file of jsonFiles) {
        processed++;
        
        if (processed % 5000 === 0) {
            console.log(`   Progress: ${processed}/${jsonFiles.length} (${foundCount} found)`);
        }

        try {
            const json = fs.readFileSync(file, 'utf8');
            const data = JSON.parse(json);

            if (!Array.isArray(data) || data.length === 0) continue;

            // Look through all objects in the file
            for (const obj of data) {
                if (!obj.Properties) continue;
                
                const props = obj.Properties;
                let resourceType = null;
                let resourceTier = null;
                let attributeCategory = null;
                let attributeSubCategory = null;
                
                // METHOD 1: Direct ResourceType property
                if (props.ResourceType) {
                    const typeStr = String(props.ResourceType);
                    if (typeStr.includes('Wood')) resourceType = 'Wood';
                    else if (typeStr.includes('Stone')) resourceType = 'Stone';
                    else if (typeStr.includes('Metal')) resourceType = 'Metal';
                }
                
                // METHOD 2: Check SerializedSparseClassData
                if (obj.SerializedSparseClassData) {
                    const sparseData = extractFromSparseData(obj.SerializedSparseClassData);
                    if (sparseData.resourceTier) resourceTier = sparseData.resourceTier;
                    if (sparseData.attributeCategory) attributeCategory = sparseData.attributeCategory;
                    if (sparseData.attributeSubCategory) attributeSubCategory = sparseData.attributeSubCategory;
                    
                    // Infer resourceType from attributeCategory if not set
                    if (!resourceType && attributeCategory) {
                        if (attributeCategory.includes('Wood')) resourceType = 'Wood';
                        else if (attributeCategory.includes('Stone')) resourceType = 'Stone';
                        else if (attributeCategory.includes('Metal')) resourceType = 'Metal';
                    }
                }
                
                // Only save if we have either resourceTier OR (resourceType AND attributeCategory)
                const isHarvestable = resourceTier || (resourceType && attributeCategory);
                
                if (isHarvestable) {
                    const objectName = obj.Name || 'Unknown';
                    const fileName = path.basename(file, '.json');
                    
                    let materialYield = 0;
                    if (resourceTier && ResourceRates[resourceTier]) {
                        materialYield = ResourceRates[resourceTier];
                    }
                    
                    harvestables.push({
                        objectName,
                        fileName,
                        filePath: path.relative(rootPath, file),
                        resourceType: resourceType || 'Unknown',
                        resourceTier: resourceTier || 'N/A',
                        materialYield,
                        attributeCategory: attributeCategory || 'N/A',
                        attributeSubCategory: attributeSubCategory || 'N/A',
                        hasExactTier: !!resourceTier
                    });
                    
                    foundCount++;
                    
                    // Log first 30 finds
                    if (foundCount <= 30) {
                        const displayName = objectName.substring(0, 35).padEnd(35);
                        const typeDisplay = (resourceType || '?').padEnd(6);
                        const tierDisplay = (resourceTier || 'N/A').padEnd(20);
                        console.log(`   ✅ ${displayName} | ${typeDisplay} | ${tierDisplay} = ${materialYield} mats`);
                    }
                }
            }
        } catch (error) {
            // Skip invalid files silently
        }
    }
    
    console.log(`\n   Final: ${processed}/${jsonFiles.length} (${foundCount} found)`);
    return harvestables;
}

function saveResults(harvestables, outputJson, outputCsv) {
    // Save JSON
    fs.writeFileSync(outputJson, JSON.stringify(harvestables, null, 2), 'utf8');
    console.log(`\n💾 Saved JSON: ${outputJson}`);

    // Save CSV
    const csvLines = [
        'ObjectName,FileName,ResourceType,ResourceTier,MaterialYield,AttributeCategory,AttributeSubCategory,HasExactTier,FilePath'
    ];
    
    for (const obj of harvestables) {
        const row = [
            `"${obj.objectName.replace(/"/g, '""')}"`,
            `"${obj.fileName}"`,
            obj.resourceType,
            `"${obj.resourceTier}"`,
            obj.materialYield,
            `"${obj.attributeCategory}"`,
            `"${obj.attributeSubCategory}"`,
            obj.hasExactTier,
            `"${obj.filePath}"`
        ].join(',');
        csvLines.push(row);
    }
    
    fs.writeFileSync(outputCsv, csvLines.join('\n'), 'utf8');
    console.log(`💾 Saved CSV: ${outputCsv}`);
}

function printSummary(harvestables) {
    console.log('\n📊 SUMMARY:');
    console.log('='.repeat(80));
    
    console.log(`\nTotal harvestables: ${harvestables.length}`);
    
    const withTier = harvestables.filter(h => h.hasExactTier).length;
    console.log(`  With exact material tier: ${withTier}`);
    console.log(`  Without exact tier: ${harvestables.length - withTier}`);
    
    // By resource type
    const byType = {};
    for (const obj of harvestables) {
        byType[obj.resourceType] = (byType[obj.resourceType] || 0) + 1;
    }
    
    console.log('\nBy resource type:');
    Object.entries(byType).sort().forEach(([type, count]) => {
        console.log(`  ${type.padEnd(10)}: ${count}`);
    });
    
    // Top resource tiers
    const tierCounts = {};
    harvestables.forEach(h => {
        if (h.resourceTier !== 'N/A') {
            tierCounts[h.resourceTier] = (tierCounts[h.resourceTier] || 0) + 1;
        }
    });
    
    console.log('\nTop resource tiers:');
    Object.entries(tierCounts)
        .sort((a, b) => b[1] - a[1])
        .slice(0, 10)
        .forEach(([tier, count]) => {
            const yield = ResourceRates[tier] || 0;
            console.log(`  ${tier.padEnd(25)}: ${count.toString().padStart(4)} objects (${yield} mats each)`);
        });
    
    console.log('\n' + '='.repeat(80));
}

function main() {
    console.log('🎮 Fortnite Harvest Data Extractor v2.0');
    console.log('='.repeat(80));
    console.log('');

    const exportPath = 'C:\\Users\\enoch\\Downloads\\FModel\\Output\\Exports';
    const resourceRatesPath = path.join(exportPath, 'FortniteGame', 'Content', 'Balance', 'DataTables', 'ResourceRates.json');
    const outputJson = 'C:\\Users\\enoch\\Downloads\\FortniteHarvestables_v2.json';
    const outputCsv = 'C:\\Users\\enoch\\Downloads\\FortniteHarvestables_v2.csv';

    // Load ResourceRates
    if (!loadResourceRates(resourceRatesPath)) {
        console.log('\n⚠️  Continuing without ResourceRates (material yields will be 0)');
    }

    // Extract harvestables
    const harvestables = extractHarvestData(exportPath);

    if (harvestables.length === 0) {
        console.log('\n❌ No harvestables found! Check that:');
        console.log('   1. FModel exports are in the correct path');
        console.log('   2. Files contain SerializedSparseClassData or ResourceType properties');
        return;
    }

    console.log(`\n✅ Found ${harvestables.length} harvestable objects!`);

    // Save results
    saveResults(harvestables, outputJson, outputCsv);
    
    // Print summary
    printSummary(harvestables);

    console.log('\n🎉 Extraction complete!');
    console.log(`\nNext steps:`);
    console.log(`  1. Open ${path.basename(outputCsv)} in Excel`);
    console.log(`  2. Filter by ResourceType (Wood/Stone/Metal)`);
    console.log(`  3. Sort by MaterialYield to see high-value targets`);
}

main();