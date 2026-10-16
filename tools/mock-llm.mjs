// Uctan uca akis dogrulamasi icin sahte LLM endpoint'i.
// Istek formati CustomEndpointProvider ile ayni: POST /call_llm -> { message }.
import http from 'node:http';

const PORT = 8765;

const entities = JSON.stringify({
  entities: [
    { name: 'Müşteri', description: 'Hizmet alan kişi', sources: ['BR-01'], confidence: 'kesin' },
    { name: 'Sipariş', description: 'Müşterinin verdiği sipariş', sources: ['BR-02'], confidence: 'kesin' }
  ]
});

const relationships = JSON.stringify({
  relationships: [
    {
      name: 'verir', from: 'Müşteri', to: 'Sipariş', cardinality: '1-N', required: true,
      description: 'Bir müşteri birden çok sipariş verir', sources: ['BR-02'], confidence: 'kesin'
    }
  ]
});

const attributes = JSON.stringify({
  entities: [
    {
      name: 'Müşteri',
      attributes: [
        { name: 'Müşteri No', primaryKey: true, required: true, unique: true, sources: ['BR-01'], confidence: 'kesin' },
        { name: 'Ünvan', required: true, sources: ['BR-01'], confidence: 'kesin' },
        { name: 'Kredi Notu', sources: [], confidence: 'belirsiz' }
      ]
    },
    {
      name: 'Sipariş',
      attributes: [
        { name: 'Sipariş No', primaryKey: true, required: true, unique: true, sources: ['BR-02'], confidence: 'kesin' },
        { name: 'Tutar', required: true, sources: ['BR-02'], confidence: 'kesin' },
        { name: 'Müşteri No', foreignKey: true, references: 'Müşteri', referencesAttribute: 'ID', required: true, sources: ['BR-02'], confidence: 'kesin' }
      ]
    }
  ]
});

const routes = [
  ['Direktif Yönlendirici', JSON.stringify({ directives: [], unsupported: [] })],
  ['Skill: Alan Ekle', JSON.stringify({ entities: [], relationships: [], notes: [] })],
  ['DEĞİŞTİRİLECEK KAPSAM', JSON.stringify({ entities: [], relationships: [], notes: [] })],
  ['Business Rule İlişki Analizi', JSON.stringify({
    relations: [{ first: 'BR-01', second: 'BR-02', kind: 'detaylandirir', explanation: 'BR-02 müşteriyi sipariş bağlamında detaylandırıyor' }]
  })],
  ['VARLIK (entity) adaylarını', entities],
  ['İLİŞKİLERİ ve KARDİNALİTELERİ', relationships],
  ['ALAN (attribute) listesini', attributes],
  ['2NF/3NF', JSON.stringify({ violations: [], notes: ['Model 3NF uyumlu görünüyor.'] })],
  ['BELİRSİZ noktaları', JSON.stringify({
    ambiguities: [{
      target: 'Musteri → Siparis', kind: 'Cardinality', impact: 'High',
      description: 'Bir siparişin birden çok müşteriye ait olup olamayacağı belirtilmemiş', confidence: 0.35
    }]
  })],
  ['HEDEFLİ SORULARI', JSON.stringify({
    questions: [{
      ambiguityId: 'A1', text: 'Bir sipariş yalnızca tek bir müşteriye mi aittir?',
      reason: 'Kardinaliteyi belirler', options: ['Evet, tek müşteriye aittir', 'Hayır, ortak sipariş olabilir']
    }]
  })],
  ['ÖZET ve ÖNERİ', JSON.stringify({
    summary: 'Müşteri varlığı BR-01, Sipariş varlığı BR-02 kaynaklı.',
    suggestions: ['Sipariş durumu için ayrı bir durum tablosu düşünülebilir']
  })]
];

const server = http.createServer((req, res) => {
  if (req.url === '/v1/models') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ data: [{ id: 'mock-model' }] }));
    return;
  }

  let body = '';
  req.on('data', chunk => { body += chunk; });
  req.on('end', () => {
    let systemPrompt = '';
    try { systemPrompt = JSON.parse(body).system_prompt || ''; } catch { /* yok say */ }

    const hit = routes.find(([marker]) => systemPrompt.includes(marker));
    const message = hit ? hit[1] : '{}';
    console.log(`${hit ? hit[0] : 'BILINMEYEN'} -> ${message.length} bayt`);

    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ message }));
  });
});

server.listen(PORT, () => console.log(`mock llm hazir: http://localhost:${PORT}`));
