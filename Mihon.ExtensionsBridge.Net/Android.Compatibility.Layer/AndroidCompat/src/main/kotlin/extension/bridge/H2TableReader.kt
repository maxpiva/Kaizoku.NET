package extension.bridge

import java.sql.DriverManager

object H2TableReader {

    fun readTable(databasePath: String, tableName: String): Array<Array<String?>> {

        val jdbcUrl = "jdbc:h2:file:$databasePath"

        Class.forName("org.h2.Driver")

        DriverManager.getConnection(jdbcUrl).use { conn ->
            conn.createStatement().use { stmt ->
                stmt.executeQuery("SELECT * FROM $tableName").use { rs ->

                    val meta = rs.metaData
                    val columnCount = meta.columnCount
                    val rows = mutableListOf<Array<String?>>()

                    // ---- Header row (remove if you don't want it) ----
                    val header = Array<String?>(columnCount) { i ->
                        meta.getColumnName(i + 1)
                    }
                    rows.add(header)

                    // ---- Data rows ----
                    while (rs.next()) {
                        val row = Array<String?>(columnCount) { i ->
                            rs.getObject(i + 1)?.toString()
                        }
                        rows.add(row)
                    }

                    return rows.toTypedArray()
                }
            }
        }
    }
}
