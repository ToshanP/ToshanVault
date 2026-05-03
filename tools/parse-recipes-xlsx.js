const ExcelJS = require('exceljs');
const fs = require('fs');
const path = require('path');

const wb = new ExcelJS.Workbook();
const xlsxPath = path.join(__dirname, '..', 'Book1.xlsx');
const outPath = path.join(__dirname, 'seed-recipes.sql');

wb.xlsx.readFile(xlsxPath).then(() => {
  const ws = wb.worksheets[0];
  const GREEN = 'FF00CC99';
  const GREEN2 = 'FF92D050';
  const SKIP_TITLES = ['Bharat Kitchen', 'For Devu'];

  function isGreenRow(row) {
    for (let c = 1; c <= 2; c++) {
      const cell = row.getCell(c);
      const fill = cell.style && cell.style.fill ? cell.style.fill : null;
      if (fill && fill.fgColor) {
        const argb = (fill.fgColor.argb || '').toUpperCase();
        if (argb === GREEN || argb === GREEN2) return true;
      }
    }
    return false;
  }

  function getUrl(cell) {
    const v = cell.value;
    if (!v) return '';
    if (typeof v === 'object' && v.hyperlink) return v.hyperlink;
    if (typeof v === 'object' && v.text) return String(v.text);
    return String(v);
  }

  const recipes = [];
  let current = null;

  ws.eachRow({ includeEmpty: false }, (row, rowNum) => {
    const cellA = row.getCell(1);
    const cellB = row.getCell(2);
    const cellC = row.getCell(3);
    const title = cellA.value ? String(cellA.value).trim() : '';
    const url = getUrl(cellB);
    const author = cellC.value ? String(cellC.value).trim() : '';
    const green = isGreenRow(row);

    if (!title && !url) return;

    if (title) {
      if (SKIP_TITLES.includes(title)) { current = null; return; }
      current = { title, urls: [], author, tried: green };
      if (url) current.urls.push(url);
      recipes.push(current);
    } else if (url && current) {
      current.urls.push(url);
      if (author && !current.author) current.author = author;
    }
  });

  // Generate SQL
  const esc = s => s.replace(/'/g, "''");
  const lines = [];
  const now = new Date().toISOString();

  function classify(title) {
    const t = (title || '').toLowerCase();
    if (/\beggs?\b|\banda\b/.test(t)) return 'Egg';
    if (/\bchicken\b|\bmurgh?\b|\bkukad\b/.test(t)) return 'Chicken';
    return 'Other';
  }

  recipes.forEach((r) => {
    const mainUrl = r.urls[0] || '';
    let notes = null;
    if (r.urls.length > 1) {
      notes = 'Alternative URLs:\n' + r.urls.slice(1).map(u => '- ' + u).join('\n');
    }
    const category = classify(r.title);

    lines.push(
      `INSERT INTO recipe (title, author, cuisine, rating, youtube_url, thumbnail_path, notes_md, is_favourite, is_tried, category, added_at) VALUES ('${esc(r.title)}', ${r.author ? "'" + esc(r.author) + "'" : 'NULL'}, 'Indian', ${r.tried ? 5 : 0}, '${esc(mainUrl)}', NULL, ${notes ? "'" + esc(notes) + "'" : 'NULL'}, ${r.tried ? 1 : 0}, ${r.tried ? 1 : 0}, '${category}', '${now}');`
    );

    // Auto-tags
    const tags = [];
    if (r.tried) tags.push('tried');
    const tl = r.title.toLowerCase();
    if (tl.includes('chicken')) tags.push('chicken');
    if (tl.includes('egg') || tl.includes('anda')) tags.push('egg');
    if (tl.includes('biryani')) tags.push('biryani');
    if (tl.includes('paneer')) tags.push('paneer');
    if (tl.includes('soup')) tags.push('soup');
    if (tl.includes('butter')) tags.push('butter');
    if (tl.includes('korma')) tags.push('korma');
    if (tl.includes('tikka')) tags.push('tikka');
    if (tl.includes('handi')) tags.push('handi');
    if (tl.includes('kheema') || tl.includes('keema')) tags.push('kheema');

    tags.forEach(tag => {
      lines.push(
        `INSERT INTO recipe_tag (recipe_id, tag) VALUES ((SELECT id FROM recipe WHERE title = '${esc(r.title)}' AND youtube_url = '${esc(mainUrl)}'), '${tag}');`
      );
    });
  });

  fs.writeFileSync(outPath, lines.join('\n'));
  console.log(`Generated ${recipes.length} recipes (${recipes.filter(r => r.tried).length} tried)`);
  console.log(`SQL written to ${outPath}`);
});
