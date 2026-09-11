import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const scriptUrl = new URL(import.meta.url);
const repositoryRoot = new URL("../../../", scriptUrl);
const schemaPath = new URL("docs/contracts/updates.schema.json", repositoryRoot);
const feedPath = new URL("docs/content/updates.json", repositoryRoot);

const [schema, feed] = await Promise.all(
  [schemaPath, feedPath].map(async (path) => JSON.parse(await readFile(path, "utf8"))),
);

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
const validate = ajv.compile(schema);

if (!validate(feed)) {
  console.error(`Invalid ${fileURLToPath(feedPath)}:`);
  for (const error of validate.errors ?? []) {
    console.error(`  ${error.instancePath || "/"} ${error.message ?? "is invalid"}`);
  }
  process.exitCode = 1;
} else {
  console.log(`${fileURLToPath(feedPath)} is valid.`);
}
