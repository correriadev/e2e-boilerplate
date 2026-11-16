import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const [run, mode] = process.argv.slice(2);
const results = path.join(run, 'results');
const files = fs.readdirSync(results).filter(f => f.endsWith('-result.json'));
const tests = files.map(f => JSON.parse(fs.readFileSync(path.join(results, f), 'utf8')));
assert(tests.length > 0, 'No tests discovered by the Allure reporter');
let attachmentCount = 0;
let sqlCommandCount = 0;
function visit(steps) {
  for (const step of steps ?? []) {
    for (const attachment of step.attachments ?? []) {
      assert(fs.existsSync(path.join(results, attachment.source)), 'Missing attachment');
      if (attachment.type === 'application/json') {
        const data = JSON.parse(fs.readFileSync(path.join(results, attachment.source), 'utf8'));
        if (attachment.name === 'Comando SQL Server') {
          const expected = data.readOnly
            ? 'SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id'
            : 'DELETE FROM dbo.Orders WHERE Id = @id AND Reference = @id';
          assert.equal(data.commandText, expected);
          assert.equal(data.executionState, 'prepared');
          assert.equal(data.parameters.length, 1);
          const parameter = data.parameters[0];
          assert.equal(parameter.name, '@id');
          assert.equal(parameter.sqlType, 'uniqueidentifier');
          assert.match(parameter.value, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
          assert(data.replaySql.includes(`DECLARE @id uniqueidentifier = '${parameter.value}';\n${expected};`));
          assert(data.replaySql.startsWith(data.readOnly ? '-- SOMENTE LEITURA' : '-- ALTERA DADOS'));
          const script = step.attachments.find(item => item.type === 'text/plain' && item.name.includes('Reproduzir'));
          assert(script, 'SQL replay script must be attached to the same step');
          assert.equal(fs.readFileSync(path.join(results, script.source), 'utf8'), data.replaySql);
          sqlCommandCount++;
        }
      }
      attachmentCount++;
    }
    visit(step.steps);
  }
}
for (const test of tests) visit(test.steps);
assert(attachmentCount > 0, 'No step attachments');
if (mode === 'Diagnostic') {
  assert.equal(tests.length, 1);
  assert.equal(tests[0].status, 'failed');
  assert.match(tests[0].statusDetails.message, /E2E_DIAGNOSTIC_EXPECTED/);
  const step = tests[0].steps.find(s => s.name.startsWith('Diagnóstico:'));
  assert.equal(step?.status, 'failed');
  assert(step.attachments.length > 0);
}
if (mode === 'Standard') {
  const journeys = tests.filter(t => t.labels?.some(l => l.name === 'epic' && l.value === 'Pedidos'));
  assert.equal(journeys.length, 2, 'Both success and external-failure journeys must be discovered');
  for (const test of journeys.filter(t => t.status === 'passed')) {
    for (const component of ['API', 'SQL Server', 'Redis'])
      assert(test.steps.some(s => s.name.includes(component) && s.attachments?.length), `Missing ${component} step evidence`);
  }
  if (journeys.every(test => test.status === 'passed'))
    assert.equal(sqlCommandCount, 4, 'Both observations and scoped cleanups must carry exact SQL and replay scripts');
}
assert(fs.existsSync(path.join(run, 'report/index.html')), 'Report HTML missing');
const reportData = path.join(run, 'report/data/test-results');
const rendered = fs.readdirSync(reportData).map(file => JSON.parse(fs.readFileSync(path.join(reportData, file), 'utf8')));
assert.equal(rendered.length, tests.length, 'Report must contain exactly the current run');
function renderedSteps(steps) {
  return (steps ?? []).flatMap(step => [step, ...renderedSteps(step.steps)]);
}
const tree = JSON.parse(fs.readFileSync(path.join(run, 'report/widgets/tree.json'), 'utf8'));
for (const raw of tests) {
  const reportTest = rendered.find(test => test.fullName === raw.fullName);
  assert(reportTest, 'Test missing from rendered report');
  const allRenderedSteps = renderedSteps(reportTest.steps);
  for (const step of renderedSteps(raw.steps)) {
    for (const attachment of step.attachments ?? []) {
      const reportStep = allRenderedSteps.find(s => s.name === step.name &&
        s.steps?.some(item => item.type === 'attachment' && item.link?.originalFileName === attachment.source));
      assert(reportStep, 'Attachment missing from its rendered step');
      const item = reportStep.steps.find(s => s.link?.originalFileName === attachment.source);
      assert.equal(item.link.missed, false);
      assert(fs.existsSync(path.join(run, 'report/data/attachments', item.link.id + item.link.ext)));
    }
  }
  let branch = tree.root;
  for (const label of ['epic', 'feature', 'story']) {
    const value = raw.labels?.find(item => item.name === label)?.value;
    assert(value, `Missing BDD label ${label}`);
    branch = branch.groups?.map(id => tree.groupsById[id]).find(group => group.name === value);
    assert(branch, `Configured BDD grouping not loaded: ${label}`);
  }
}
const secret = process.env.E2E_SYNTHETIC_SECRET ?? 'local-evidence-canary';
const forbidden = [secret, process.env.SQLSERVER_SA_PASSWORD].filter(Boolean);
function scan(directory) {
  for (const item of fs.readdirSync(directory, {withFileTypes:true})) {
    const file = path.join(directory, item.name);
    if (item.isDirectory()) scan(file);
    else {
      const data = fs.readFileSync(file);
      for (const value of forbidden)
        for (const encoding of ['utf8', 'utf16le'])
          assert(!data.includes(Buffer.from(value, encoding)), `Secret detected in artifact: ${item.name}`);
    }
  }
}
scan(run);
console.log(`Verified ${tests.length} tests, ${attachmentCount} step attachments, ${sqlCommandCount} SQL replay commands, report HTML and secret absence.`);
