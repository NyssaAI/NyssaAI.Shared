# Responsibility: Low-level command and query execution logic.
# Public interface:
# `-- DbCommandExecutor
#     +-- execute_ad_hoc(conn, dialect, timeout, sql, params)
#     +-- execute_ad_hoc_query(conn, dialect, timeout, sql, params)
#     +-- execute_command(conn, dialect, timeout, command)
#     +-- execute_non_query(conn, dialect, timeout, command)
#     +-- execute_query(conn, dialect, timeout, query)
#     `-- execute_scalar(conn, dialect, timeout, sql, params)
"""Low-level command executor coordinating dialect preparation and parameter translation."""

from typing import Mapping, Sequence, TypeVar
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.db_command import DbCommand
from nyssa_ai_dbgateway.commands.db_non_query import DbNonQuery
from nyssa_ai_dbgateway.commands.db_query import DbQuery
from nyssa_ai_dbgateway.commands.db_parameter_binder import DbParameterBinder
from nyssa_ai_dbgateway.commands.db_record_reader import DbRecordReader
from nyssa_ai_dbgateway.commands.rewrite_sql_parameters import rewrite_sql_parameters
from nyssa_ai_dbgateway.providers.db_connection_adapter import DbConnectionAdapter

TResult = TypeVar("TResult")
TRow = TypeVar("TRow")


class DbCommandExecutor:
    """Executes commands and queries against a connection adapter."""

    @staticmethod
    def execute_non_query(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        command: DbNonQuery,
    ) -> int:
        raw_sql = command.get_sql(dialect)
        binder = DbParameterBinder()
        command.bind_parameters(binder, dialect)
        return DbCommandExecutor._dispatch_non_query(conn, dialect, timeout, raw_sql, binder)

    @staticmethod
    def execute_command(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        command: DbCommand[TResult],
    ) -> TResult:
        raw_sql = command.get_sql(dialect)
        binder = DbParameterBinder()
        command.bind_parameters(binder, dialect)
        columns, rows = DbCommandExecutor._dispatch_query(conn, dialect, timeout, raw_sql, binder)
        first_row = rows[0] if rows else ()
        reader = DbRecordReader(first_row, columns, dialect)
        return command.map_result(reader)

    @staticmethod
    def execute_query(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        query: DbQuery[TRow],
    ) -> list[TRow]:
        raw_sql = query.get_sql(dialect)
        binder = DbParameterBinder()
        query.bind_parameters(binder, dialect)
        columns, rows = DbCommandExecutor._dispatch_query(conn, dialect, timeout, raw_sql, binder)
        results: list[TRow] = []
        for r in rows:
            reader = DbRecordReader(r, columns, dialect)
            results.append(query.map_row(reader))
        return results

    @staticmethod
    def execute_ad_hoc(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> int:
        binder = DbParameterBinder()
        if isinstance(params, Mapping):
            binder.add_many(params)
        elif isinstance(params, Sequence):
            for i, v in enumerate(params):
                binder.add(f"p{i}", v)
        return DbCommandExecutor._dispatch_non_query(conn, dialect, timeout, sql, binder)

    @staticmethod
    def execute_scalar(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        binder = DbParameterBinder()
        if isinstance(params, Mapping):
            binder.add_many(params)
        elif isinstance(params, Sequence):
            for i, v in enumerate(params):
                binder.add(f"p{i}", v)

        if dialect == "postgres":
            rewritten_sql, param_names = rewrite_sql_parameters(sql)
            pg_values = binder.to_postgres_values(param_names)
            return conn.execute_scalar(rewritten_sql, pg_values, timeout)
        sqlite_params = binder.to_sqlite_dict()
        return conn.execute_scalar(sql, sqlite_params if sqlite_params else None, timeout)

    @staticmethod
    def execute_ad_hoc_query(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> list[dict[str, object]]:
        binder = DbParameterBinder()
        if isinstance(params, Mapping):
            binder.add_many(params)
        elif isinstance(params, Sequence):
            for i, v in enumerate(params):
                binder.add(f"p{i}", v)
        columns, rows = DbCommandExecutor._dispatch_query(conn, dialect, timeout, sql, binder)
        out: list[dict[str, object]] = []
        for r in rows:
            out.append(dict(zip(columns, r)))
        return out

    @staticmethod
    def _dispatch_non_query(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        sql: str,
        binder: DbParameterBinder,
    ) -> int:
        if dialect == "postgres":
            rewritten_sql, param_names = rewrite_sql_parameters(sql)
            pg_values = binder.to_postgres_values(param_names)
            return conn.execute_non_query(rewritten_sql, pg_values, timeout)
        sqlite_params = binder.to_sqlite_dict()
        return conn.execute_non_query(sql, sqlite_params if sqlite_params else None, timeout)

    @staticmethod
    def _dispatch_query(
        conn: DbConnectionAdapter,
        dialect: DbDialect,
        timeout: int,
        sql: str,
        binder: DbParameterBinder,
    ) -> tuple[list[str], list[tuple[object, ...]]]:
        if dialect == "postgres":
            rewritten_sql, param_names = rewrite_sql_parameters(sql)
            pg_values = binder.to_postgres_values(param_names)
            return conn.execute_query(rewritten_sql, pg_values, timeout)
        sqlite_params = binder.to_sqlite_dict()
        return conn.execute_query(sql, sqlite_params if sqlite_params else None, timeout)
