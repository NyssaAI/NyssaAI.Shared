# Responsibility: Unit test parameter binding, placeholder rewriting, and value coercion.
# Public interface:
# +-- test_db_parameter_binder_invalid_name_raises_error()
# +-- test_db_parameter_binder_missing_parameter_raises_error()
# +-- test_db_parameter_binder_postgres_ordering()
# +-- test_db_parameter_binder_sqlite_coercion()
# +-- test_rewrite_sql_parameters_ignores_literals_and_comments()
# `-- test_rewrite_sql_parameters_rewrites_at_placeholders_to_positional()
"""Unit tests for parameter binding."""

from datetime import datetime, timezone
from decimal import Decimal
from uuid import UUID
import pytest
from nyssa_ai_dbgateway.commands.db_parameter_binder import DbParameterBinder
from nyssa_ai_dbgateway.commands.rewrite_sql_parameters import rewrite_sql_parameters


def test_rewrite_sql_parameters_rewrites_at_placeholders_to_positional() -> None:
    sql = "SELECT * FROM users WHERE name = @name AND age > @age AND dept = @name;"
    rewritten, params = rewrite_sql_parameters(sql)
    assert rewritten == "SELECT * FROM users WHERE name = $1 AND age > $2 AND dept = $1;"
    assert params == ["name", "age"]


def test_rewrite_sql_parameters_ignores_literals_and_comments() -> None:
    sql = "SELECT '@not_param', /* @comment */ -- @line_comment\n @real_param;"
    rewritten, params = rewrite_sql_parameters(sql)
    assert rewritten == "SELECT '@not_param', /* @comment */ -- @line_comment\n $1;"
    assert params == ["real_param"]


def test_db_parameter_binder_invalid_name_raises_error() -> None:
    binder = DbParameterBinder()
    for invalid in ("", "   ", "@", "  @  ", ":"):
        with pytest.raises(ValueError, match="Parameter name cannot be empty"):
            binder.add(invalid, "val")


def test_db_parameter_binder_sqlite_coercion() -> None:
    binder = DbParameterBinder()
    binder.add("  @active  ", True)
    binder.add("inactive", False)
    binder.add("amount", Decimal("19.99"))
    uid = UUID("12345678-1234-5678-1234-567812345678")
    binder.add("id", uid)
    now = datetime(2026, 10, 31, 12, 0, 0, tzinfo=timezone.utc)
    binder.add("created", now)
    binder.add("blob", bytearray([1, 2, 3]))
    binder.add("empty", None)

    sqlite_dict = binder.to_sqlite_dict()
    assert sqlite_dict["active"] == 1
    assert sqlite_dict["inactive"] == 0
    assert sqlite_dict["amount"] == "19.99"
    assert sqlite_dict["id"] == str(uid)
    assert "2026-10-31" in str(sqlite_dict["created"])
    assert sqlite_dict["blob"] == b"\x01\x02\x03"
    assert sqlite_dict["empty"] is None


def test_db_parameter_binder_postgres_ordering() -> None:
    binder = DbParameterBinder()
    binder.add("b", 2)
    binder.add("a", 1)
    binder.add("c", 3)

    pg_vals = binder.to_postgres_values(["a", "c", "b"])
    assert pg_vals == [1, 3, 2]


def test_db_parameter_binder_missing_parameter_raises_error() -> None:
    binder = DbParameterBinder()
    binder.add("id", 1)
    _, params = rewrite_sql_parameters("SELECT * FROM items WHERE id = @id AND missing = @missing")
    with pytest.raises(ValueError, match="Missing parameter value for 'missing'"):
        binder.to_postgres_values(params)
