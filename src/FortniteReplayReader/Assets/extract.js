const fs = require('fs');
const path = require('path');

let ResourceRates = {};

function loadResourceRates(filePath) {
    try {
        console.log('📖 Loading ResourceRates.json...');
        
        if (!fs.existsSync(filePath)) {
            console.log(`❌ ResourceRates.json not found at: ${filePath}`);
            return false;
        }

        const json = fs.readFileSync(filePath, 'utf8');
        const data = JSON.parse(json);

        if (!Array.isArray(data) || data.length === 0) {
            console.log('❌ Invalid ResourceRates.json format');
            return false;
        }

        const root = data[0];
        if (!root.Rows) {
            console.log("❌ No 'Rows' property found in ResourceRates.json");
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

        console.log(`✅ Loaded ${Object.keys(ResourceRates).length} resource tiers\n`);
        return Object.keys(ResourceRates).length > 0;
    } catch (error) {
        console.log(`❌ Error loading ResourceRates.json: ${error.message}`);
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
        } else if (file.endsWith('.json') && !file.includes('ResourceRates')) {
            fileList.push(filePath);
        }
    });

    return fileList;
}

// 🔍 ANALYZE MODE: Find all possible harvest-related properties
function analyzeHarvestProperties(rootPath) {
    console.log('🔍 ANALYSIS MODE: Searching for harvest-related properties...\n');
    
    const jsonFiles = getAllJsonFiles(rootPath);
    console.log(`📄 Analyzing ${jsonFiles.length} JSON files...\n`);

    const propertyStats = {};
    const exampleFiles = {};
    
    let processed = 0;

    for (const file of jsonFiles) {
        processed++;
        
        if (processed % 1000 === 0) {
            console.log(`   Progress: ${processed}/${jsonFiles.length}`);
        }

        try {
            const json = fs.readFileSync(file, 'utf8');
            const data = JSON.parse(json);

            if (!Array.isArray(data) || data.length === 0) continue;
            const root = data[0];
            if (!root.Properties) continue;

            const properties = root.Properties;

            // Search for ANY harvest-related properties
            const harvestKeywords = [
                'Resource', 'Building', 'Harvest', 'Material', 
                'Wood', 'Stone', 'Metal', 'Health', 'Attribute',
                'Loot', 'Destruction', 'Weak', 'Amount'
            ];

            for (const prop of Object.keys(properties)) {
                // Check if property name contains any harvest keyword
                const isHarvestRelated = harvestKeywords.some(keyword => 
                    prop.toLowerCase().includes(keyword.toLowerCase())
                );

                if (isHarvestRelated) {
                    if (!propertyStats[prop]) {
                        propertyStats[prop] = 0;
                        exampleFiles[prop] = file;
                    }
                    propertyStats[prop]++;
                }
            }
        } catch (error) {
            // Skip invalid files
        }
    }

    // Print results
    console.log('\n📊 HARVEST-RELATED PROPERTIES FOUND:\n');
    console.log('='.repeat(80));
    
    const sortedProps = Object.entries(propertyStats).sort((a, b) => b[1] - a[1]);
    
    for (const [prop, count] of sortedProps) {
        console.log(`\n${prop.padEnd(40)} - Found in ${count} files`);
        console.log(`   Example: ${path.relative(rootPath, exampleFiles[prop])}`);
    }
    
    console.log('\n' + '='.repeat(80));
    console.log(`\n✅ Found ${sortedProps.length} unique harvest-related properties`);
    
    return { propertyStats, exampleFiles };
}

// 🔍 DEEP DIVE: Examine specific files with harvest properties
function deepDiveFiles(rootPath, propertyStats, exampleFiles) {
    console.log('\n\n🔬 DEEP DIVE: Examining example files...\n');
    console.log('='.repeat(80));

    // Get top 10 most common properties
    const topProps = Object.entries(propertyStats)
        .sort((a, b) => b[1] - a[1])
        .slice(0, 10);

    for (const [prop, count] of topProps) {
        const file = exampleFiles[prop];
        
        try {
            const json = fs.readFileSync(file, 'utf8');
            const data = JSON.parse(json);
            const root = data[0];
            const propValue = root.Properties[prop];

            console.log(`\n📄 Property: ${prop}`);
            console.log(`   File: ${path.basename(file)}`);
            console.log(`   Value type: ${typeof propValue}`);
            console.log(`   Value: ${JSON.stringify(propValue, null, 2).substring(0, 200)}`);
            
            if (propValue && typeof propValue === 'object') {
                console.log(`   Sub-properties: ${Object.keys(propValue).join(', ')}`);
            }
        } catch (error) {
            console.log(`   Error reading file: ${error.message}`);
        }
    }
    
    console.log('\n' + '='.repeat(80));
}

// 🎯 EXTRACT with discovered patterns
function extractWithPatterns(rootPath, patterns) {
    console.log('\n\n🎯 EXTRACTING HARVESTABLES with discovered patterns...\n');
    
    const jsonFiles = getAllJsonFiles(rootPath);
    const harvestables = [];
    let processed = 0;

    for (const file of jsonFiles) {
        processed++;
        
        if (processed % 1000 === 0) {
            console.log(`   Progress: ${processed}/${jsonFiles.length} (${harvestables.length} found)`);
        }

        try {
            const json = fs.readFileSync(file, 'utf8');
            const data = JSON.parse(json);

            if (!Array.isArray(data) || data.length === 0) continue;
            const root = data[0];
            if (!root.Properties) continue;

            const properties = root.Properties;

            // Check multiple possible patterns
            let resourceTier = null;
            let resourceType = null;
            let maxHealth = 0;

            // Pattern 1: BuildingResourceAmountOverride
            if (properties.BuildingResourceAmountOverride?.RowName) {
                resourceTier = properties.BuildingResourceAmountOverride.RowName;
            }

            // Pattern 2: ResourceType
            if (properties.ResourceType) {
                const typeStr = String(properties.ResourceType);
                if (typeStr.includes('Wood')) resourceType = 'Wood';
                else if (typeStr.includes('Stone')) resourceType = 'Stone';
                else if (typeStr.includes('Metal')) resourceType = 'Metal';
            }

            // Pattern 3: AttributeInitKeys
            if (properties.AttributeInitKeys?.AttributeInitCategory) {
                const category = properties.AttributeInitKeys.AttributeInitCategory;
                if (!resourceType) {
                    if (category.includes('Wood')) resourceType = 'Wood';
                    else if (category.includes('Stone')) resourceType = 'Stone';
                    else if (category.includes('Metal')) resourceType = 'Metal';
                }
            }

            // Pattern 4: MaxHealth
            if (properties.MaxHealth !== undefined) {
                maxHealth = properties.MaxHealth;
            }

            // Accept if has resourceTier OR (resourceType AND health > 0)
            const isHarvestable = resourceTier || (resourceType && maxHealth > 0);

            if (isHarvestable) {
                const objectName = root.Name || 'Unknown';
                const archetypePath = path.basename(file, '.json');
                
                let materialYield = 0;
                if (resourceTier && ResourceRates[resourceTier]) {
                    materialYield = ResourceRates[resourceTier];
                } else if (maxHealth > 0 && !resourceTier) {
                    // Estimate from health if no tier
                    materialYield = Math.round(maxHealth / 10);
                }

                harvestables.push({
                    objectName,
                    archetypePath,
                    resourceType: resourceType || 'Unknown',
                    resourceTier: resourceTier || 'Estimated',
                    materialYield,
                    maxHealth,
                    hasExactTier: !!resourceTier
                });

                // Log first 20
                if (harvestables.length <= 20) {
                    console.log(`   ✅ ${objectName.substring(0, 40).padEnd(40)} → ${resourceTier || 'estimated'} = ${materialYield} ${resourceType}`);
                }
            }
        } catch (error) {
            // Skip
        }
    }

    return harvestables;
}

function saveResults(harvestables, outputJson, outputCsv) {
    // Save JSON
    fs.writeFileSync(outputJson, JSON.stringify(harvestables, null, 2), 'utf8');
    console.log(`\n💾 Saved JSON: ${outputJson}`);

    // Save CSV
    const lines = ['ObjectName,ArchetypePath,ResourceType,ResourceTier,MaterialYield,MaxHealth,HasExactTier'];
    for (const obj of harvestables) {
        lines.push(`"${obj.objectName}","${obj.archetypePath}",${obj.resourceType},${obj.resourceTier},${obj.materialYield},${obj.maxHealth},${obj.hasExactTier}`);
    }
    fs.writeFileSync(outputCsv, lines.join('\n'), 'utf8');
    console.log(`💾 Saved CSV: ${outputCsv}`);
}

function printSummary(harvestables) {
    console.log('\n📊 SUMMARY:');
    console.log('='.repeat(80));
    
    const withExactTier = harvestables.filter(h => h.hasExactTier).length;
    const estimated = harvestables.length - withExactTier;
    
    console.log(`\nTotal harvestables: ${harvestables.length}`);
    console.log(`  With exact tier: ${withExactTier}`);
    console.log(`  Estimated: ${estimated}`);
    
    // By resource type
    const byType = {};
    for (const obj of harvestables) {
        byType[obj.resourceType] = (byType[obj.resourceType] || 0) + 1;
    }
    
    console.log('\nBy resource type:');
    for (const [type, count] of Object.entries(byType).sort()) {
        console.log(`  ${type}: ${count}`);
    }
    
    console.log('\n' + '='.repeat(80));
}

function main() {
    console.log('🎮 Fortnite Harvest Data Extractor - DISCOVERY MODE');
    console.log('='.repeat(80));
    console.log('\n');

    const exportPath = 'C:\\Users\\enoch\\Downloads\\FModel\\Output\\Exports';
    const resourceRatesPath = 'C:\\Users\\enoch\\Downloads\\FModel\\Output\\Exports\\FortniteGame\\Content\\Balance\\DataTables\\ResourceRates.json';
    const outputJson = 'C:\\Users\\enoch\\Downloads\\FortniteHarvestables.json';
    const outputCsv = 'C:\\Users\\enoch\\Downloads\\FortniteHarvestables.csv';

    // Load ResourceRates
    if (!loadResourceRates(resourceRatesPath)) {
        console.log('❌ Failed to load ResourceRates.json!');
        return;
    }

    // STEP 1: Analyze all properties
    const { propertyStats, exampleFiles } = analyzeHarvestProperties(exportPath);

    // STEP 2: Deep dive into examples
    deepDiveFiles(exportPath, propertyStats, exampleFiles);

    // STEP 3: Extract with all patterns
    const harvestables = extractWithPatterns(exportPath);

    console.log(`\n✅ Found ${harvestables.length} harvestable objects!`);

    // Save and summarize
    if (harvestables.length > 0) {
        saveResults(harvestables, outputJson, outputCsv);
        printSummary(harvestables);
    }

    console.log('\n🎉 Done!');
}

main();