const fs=require('node:fs'),assert=require('node:assert/strict');
const translations=JSON.parse(fs.readFileSync('Rbrowser.Core/Resources/strings.en.json','utf8'));let checks=0;
const files=['TvBrowser.cs','TvVideoPlayer.cs','TvVoiceInput.cs','TvLocalization.cs','TvStartup.cs','FilterService.cs'];const keys=new Set();
for(const file of files){const source=fs.readFileSync('Rbrowser/Platforms/Android/'+file,'utf8');for(const match of source.matchAll(/I18n\.(?:Text|Format)\("((?:\\.|[^"\\])*)"/g))keys.add(JSON.parse('"'+match[1]+'"'));}
for(const key of keys){assert.ok(translations[key],`Missing English translation: ${key}`);assert.deepEqual([...key.matchAll(/\{\d+[^}]*\}/g)].map(m=>m[0]).sort(),[...translations[key].matchAll(/\{\d+[^}]*\}/g)].map(m=>m[0]).sort(),`Format arguments differ: ${key}`);checks++;}
assert.ok(keys.size>=140,'all UI areas included');assert.ok(!Object.values(translations).filter(v=>v!=='Русский').some(v=>/[А-Яа-яЁё]/.test(v)),'English values contain no untranslated Russian UI');checks+=2;
console.log(`PASS: ${checks} localization checks`);
