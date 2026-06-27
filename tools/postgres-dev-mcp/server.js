#!/usr/bin/env node

const { McpServer } = require("@modelcontextprotocol/sdk/server/mcp.js");
const { StdioServerTransport } = require("@modelcontextprotocol/sdk/server/stdio.js");
const { z } = require("zod");
const { Pool } = require("pg");

const DATABASE_URL = process.env.DATABASE_URL;
const ALLOW_WRITES = String(process.env.ALLOW_WRITES || "false").toLowerCase() === "true";
const ENVIRONMENT_NAME = process.env.ENVIRONMENT_NAME || "Development";

if (!DATABASE_URL) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

if (!["development", "dev", "local"].includes(ENVIRONMENT_NAME.toLowerCase())) {
  console.error(`Refusing to start postgres-dev-mcp outside development. ENVIRONMENT_NAME=${ENVIRONMENT_NAME}`);
  process.exit(1);
}

const pool = new Pool({ connectionString: DATABASE_URL });

const server = new McpServer({
  name: "postgres-dev-mcp",
  version: "1.0.0",
});

function textResult(value) {
  return {
    content: [
      {
        type: "text",
        text: typeof value === "string" ? value : JSON.stringify(value, null, 2),
      },
    ],
  };
}

function firstSqlKeyword(sql) {
  return (sql || "").trim().split(/\s+/)[0]?.toLowerCase() || "";
}

function assertSelectOnly(sql) {
  const keyword = firstSqlKeyword(sql);
  if (!["select", "with", "show", "explain"].includes(keyword)) {
    throw new Error("pg_query is read-only. Use pg_execute for development writes.");
  }
}

function assertDevelopmentWrite(sql, confirm) {
  if (!ALLOW_WRITES) {
    throw new Error("Writes are disabled. Set ALLOW_WRITES=true only for local development.");
  }

  if (confirm !== "DEVELOPMENT_DB_WRITE") {
    throw new Error('Writes require confirm: "DEVELOPMENT_DB_WRITE".');
  }

  const keyword = firstSqlKeyword(sql);
  if (!["insert", "update", "delete"].includes(keyword)) {
    throw new Error("pg_execute only allows INSERT, UPDATE, DELETE.");
  }

  const lowered = sql.toLowerCase();
  const blocked = [
    "drop ",
    "truncate ",
    "alter ",
    "create ",
    "grant ",
    "revoke ",
    "copy ",
    "vacuum ",
    "reindex ",
  ];

  if (blocked.some((word) => lowered.includes(word))) {
    throw new Error("Dangerous schema/admin SQL is blocked in this development MCP.");
  }
}

server.tool(
  "pg_query",
  "Run a read-only SQL query against the local development PostgreSQL database. Allows SELECT/WITH/SHOW/EXPLAIN only.",
  {
    sql: z.string().min(1),
    params: z.array(z.any()).optional().default([]),
    maxRows: z.number().int().min(1).max(500).optional().default(100),
  },
  async ({ sql, params, maxRows }) => {
    assertSelectOnly(sql);
    const limitedSql = `SELECT * FROM (${sql.replace(/;+\s*$/, "")}) AS codex_mcp_query LIMIT ${maxRows}`;
    const result = await pool.query(limitedSql, params);
    return textResult({
      rowCount: result.rowCount,
      rows: result.rows,
    });
  }
);

server.tool(
  "pg_execute",
  "Run an INSERT/UPDATE/DELETE against the local development PostgreSQL database. Requires confirm=DEVELOPMENT_DB_WRITE.",
  {
    sql: z.string().min(1),
    params: z.array(z.any()).optional().default([]),
    confirm: z.string(),
  },
  async ({ sql, params, confirm }) => {
    assertDevelopmentWrite(sql, confirm);
    const client = await pool.connect();
    try {
      await client.query("BEGIN");
      const result = await client.query(sql, params);
      await client.query("COMMIT");
      return textResult({
        command: result.command,
        rowCount: result.rowCount,
      });
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally {
      client.release();
    }
  }
);

server.tool(
  "pg_tables",
  "List public tables in the local development PostgreSQL database.",
  {},
  async () => {
    const result = await pool.query(`
      SELECT table_name
      FROM information_schema.tables
      WHERE table_schema = 'public'
      ORDER BY table_name;
    `);
    return textResult(result.rows);
  }
);

server.tool(
  "pg_describe",
  "Describe columns for one public table in the local development PostgreSQL database.",
  {
    table: z.string().min(1),
  },
  async ({ table }) => {
    const result = await pool.query(
      `
      SELECT
        column_name,
        data_type,
        is_nullable,
        column_default
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = $1
      ORDER BY ordinal_position;
      `,
      [table]
    );
    return textResult(result.rows);
  }
);

process.on("SIGINT", async () => {
  await pool.end();
  process.exit(0);
});

process.on("SIGTERM", async () => {
  await pool.end();
  process.exit(0);
});

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
