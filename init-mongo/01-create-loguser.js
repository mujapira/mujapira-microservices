const logDbName = process.env.LOG_DB_NAME || "LogServiceDb";
const logUser = process.env.LOG_DB_USER || "logservice";
const logPass = process.env.LOG_DB_PASSWORD || "senhaLogServiceSegura!";

const logDb = db.getSiblingDB(logDbName);
logDb.createUser({
  user: logUser,
  pwd: logPass,
  roles: [
    { role: "readWrite", db: logDbName }
  ]
});
